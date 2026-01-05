using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;

namespace RetroDisplay
{
    public sealed class Dx11Renderer : IDisposable
    {
        private readonly object _d3dLock = new();
        private readonly object _frameLock = new();

        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGISwapChain1? _swapChain;
        private ID3D11RenderTargetView? _rtv;

        // Video textures
        private ID3D11Texture2D? _videoTex;     // DEFAULT (GPU)
        private ID3D11Texture2D? _stagingTex;   // STAGING (CPU write)
        private int _videoW, _videoH;

        // Quad rendering
        private ID3D11VertexShader? _vs;
        private ID3D11PixelShader? _ps;
        private ID3D11InputLayout? _inputLayout;
        private ID3D11Buffer? _vertexBuffer;
        private ID3D11SamplerState? _sampler;
        private ID3D11ShaderResourceView? _videoSrv;

        //CRT Shader
        private ID3D11PixelShader? _psCopy;
        private ID3D11PixelShader? _psCrt;
        private ID3D11Buffer? _crtCBuffer;
        private CrtParams _crt;

        // Viewport tracking (used by CRT shader)
        private int _viewportWidth;
        private int _viewportHeight;


        [StructLayout(LayoutKind.Sequential)]
        private struct CrtParams
        {
            public float Brightness;
            public float Contrast;
            public float Saturation;
            public float ScanlineStrength;

            public float Gamma;
            public float PhosphorStrength;
            public float ScreenWidth;
            public float ScreenHeight;

            public float EffectiveWidth;
            public float EffectiveHeight;
            public float ScanlinePhase;
            public float MaskType;

            public float BeamWidth;
            public float HSize;     // NEW
            public float VSize;     // NEW
            public float Pad0;
        }



        // Backbuffer size
        private int _bbW, _bbH;


        // Latest frame data (written by capture thread, consumed by render thread)
        private byte[]? _pendingFrame;
        private int _pendingW, _pendingH, _pendingStride;
        private int _frameReady; // 0/1

        // Render thread
        private Thread? _renderThread;
        private volatile bool _running;

        // State
        private volatile bool _hasVideo;

        // Stats
        private long _lastPresentTicks;

        public void Initialize(IntPtr hwnd, int width, int height)
        {
            lock (_d3dLock)
            {
                if (_device != null) return;

                var deviceFlags = DeviceCreationFlags.BgraSupport;

#if DEBUG
                deviceFlags |= DeviceCreationFlags.Debug;
#endif

                D3D11CreateDevice(
                    null,
                    DriverType.Hardware,
                    deviceFlags,
                    null,
                    out _device,
                    out _context);

                // Enable multithread protection (good practice even though we keep context on one thread)
                using (var mt = _context.QueryInterfaceOrNull<ID3D11Multithread>())
                {
                    if (mt != null) mt.SetMultithreadProtected(true);
                }

                using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
                using var adapter = dxgiDevice.GetAdapter();
                using var factory = adapter.GetParent<IDXGIFactory2>();

                var scDesc = new SwapChainDescription1
                {
                    Width = Math.Max(1,(uint)width),
                    Height = Math.Max(1, (uint)height),
                    Format = Format.B8G8R8A8_UNorm,
                    Stereo = false,
                    SampleDescription = new SampleDescription(1, 0),
                    BufferUsage = Usage.RenderTargetOutput,
                    BufferCount = 2,
                    Scaling = Scaling.Stretch,
                    SwapEffect = SwapEffect.FlipDiscard,
                    AlphaMode = AlphaMode.Ignore,
                    Flags = SwapChainFlags.None
                };

                _swapChain = factory.CreateSwapChainForHwnd(_device, hwnd, scDesc);
                _bbW = (int)scDesc.Width;
                _bbH = (int)scDesc.Height;
                uint cbSize = (uint)Marshal.SizeOf<CrtParams>(); // now 64 bytes

                CreateOrUpdateRtv();
                CreateShaders();
                CreateQuad();

                _crtCBuffer?.Dispose();
                _crtCBuffer = _device!.CreateBuffer(new BufferDescription(
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf<CrtParams>(),
                    BindFlags.ConstantBuffer,
                    ResourceUsage.Dynamic,
                    CpuAccessFlags.Write));
            }
        }

        public void Start()
        {
            if (_renderThread != null) return;

            _running = true;
            _renderThread = new Thread(RenderLoop)
            {
                IsBackground = true,
                Name = "DX11 Render Thread"
            };
            _renderThread.Start();
        }

        public void Stop()
        {
            _running = false;
            _renderThread?.Join();
            _renderThread = null;
        }

        private void CreateShaders()
        {
            const string vsSrc = @"
struct VS_IN { float2 pos : POSITION; float2 uv : TEXCOORD0; };
struct VS_OUT { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
VS_OUT main(VS_IN input)
{
    VS_OUT o;
    o.pos = float4(input.pos, 0, 1);
    o.uv  = input.uv;
    return o;
}";

            const string psCopySrc = @"
Texture2D tex0 : register(t0);
SamplerState samp0 : register(s0);
float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
{
    return tex0.Sample(samp0, uv);
}";

            // Load CRT HLSL from file
            // Make sure the file is copied to output (see step 2 below)
            string crtPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "RetroCrt.hlsl");
            string psCrtSrc = File.ReadAllText(crtPath);

            ReadOnlyMemory<byte> vsMem = Compiler.Compile(vsSrc, "main", "FullscreenQuadVS", "vs_4_0", ShaderFlags.None);
            ReadOnlyMemory<byte> psCopyMem = Compiler.Compile(psCopySrc, "main", "CopyPS", "ps_4_0", ShaderFlags.None);
            ReadOnlyMemory<byte> psCrtMem = Compiler.Compile(psCrtSrc, "main", "CrtPS", "ps_4_0", ShaderFlags.None);

            byte[] vsBytes = vsMem.ToArray();
            byte[] psCopyBytes = psCopyMem.ToArray();
            byte[] psCrtBytes = psCrtMem.ToArray();

            _vs = _device!.CreateVertexShader(vsBytes);

            _psCopy?.Dispose();
            _psCrt?.Dispose();
            _psCopy = _device.CreatePixelShader(psCopyBytes);
            _psCrt = _device.CreatePixelShader(psCrtBytes);

            _inputLayout?.Dispose();
            _inputLayout = _device.CreateInputLayout(
                new[]
                {
            new InputElementDescription("POSITION", 0, Vortice.DXGI.Format.R32G32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Vortice.DXGI.Format.R32G32_Float, 8, 0),
                },
                vsBytes
            );

            CreateCrtConstantBuffer();
        }


        private void CreateCrtConstantBuffer()
        {
            _crtCBuffer?.Dispose();

            uint size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<CrtParams>();

            _crtCBuffer = _device!.CreateBuffer(
                new BufferDescription(
                    size,
                    BindFlags.ConstantBuffer,
                    ResourceUsage.Dynamic,
                    CpuAccessFlags.Write
                )
            );
        }


        private unsafe void CreateQuad()
        {
            float[] vertices =
            {
        // x, y,   u, v
        -1f, -1f,  0f, 1f,
        -1f,  1f,  0f, 0f,
         1f, -1f,  1f, 1f,
         1f,  1f,  1f, 0f,
    };

            var desc = new BufferDescription(
                (uint)(sizeof(float) * vertices.Length),
                BindFlags.VertexBuffer,
                ResourceUsage.Immutable);

            fixed (float* p = vertices)
            {
                var init = new SubresourceData((IntPtr)p);
                _vertexBuffer = _device!.CreateBuffer(desc, init);
            }

            _sampler = _device!.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp
            });
        }



        /// <summary>
        /// Called from capture thread(s). No D3D calls here.
        /// Stores the latest frame only (drops older frames).
        /// bgra must be BGRA32 tightly packed with 'stride' bytes per row.
        /// </summary>
        public void SubmitFrameBgra32(byte[] bgra, int width, int height, int stride)
        {
            if (bgra == null || bgra.Length == 0) return;
            if (width <= 0 || height <= 0 || stride <= 0) return;

            lock (_frameLock)
            {
                _pendingFrame = bgra;
                _pendingW = width;
                _pendingH = height;
                _pendingStride = stride;
            }

            Interlocked.Exchange(ref _frameReady, 1);
        }

        public void Resize(int width, int height)
        {
            lock (_d3dLock)
            {
                if (_swapChain == null || _context == null) return;

                width = Math.Max(1, width);
                height = Math.Max(1, height);

                _viewportWidth = width;
                _viewportHeight = height;

                if (width == _bbW && height == _bbH) return;

                _rtv?.Dispose();
                _rtv = null;

                _swapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None);
                _bbW = width;
                _bbH = height;
                _viewportWidth = width;
                _viewportHeight = height;

                CreateOrUpdateRtv();
            }
        }

        public void ResetVideo()
        {
            _hasVideo = false;
            lock (_frameLock)
            {
                _pendingFrame = null;
                _pendingW = _pendingH = _pendingStride = 0;
            }
            Interlocked.Exchange(ref _frameReady, 0);
        }

        private void RenderLoop()
        {
            // Simple vsync pacing: Present blocks if vsync=1.
            // If you want uncapped, pass 0 to Present later.
            while (_running)
            {
                try
                {
                    RenderFrame();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("DX11 RenderLoop exception: " + ex);
                    // If you want to bubble this up, you can add an event callback.
                    // For now, keep the loop alive.
                }
            }
        }

        private void RenderFrame()
        {
            ID3D11DeviceContext? ctx;
            IDXGISwapChain1? sc;
            ID3D11RenderTargetView? rtv;

            lock (_d3dLock)
            {
                ctx = _context;
                sc = _swapChain;
                rtv = _rtv;
            }

            if (ctx == null || sc == null || rtv == null)
            {
                Thread.Sleep(5);
                return;
            }

            // Pull latest frame (if any) and upload ON THE RENDER THREAD (safe)
            if (Interlocked.Exchange(ref _frameReady, 0) == 1)
            {
                byte[]? frame;
                int w, h, stride;

                lock (_frameLock)
                {
                    frame = _pendingFrame;
                    w = _pendingW;
                    h = _pendingH;
                    stride = _pendingStride;
                }

                if (frame != null && w > 0 && h > 0 && stride > 0)
                {
                    UploadFrameOnRenderThread(frame, w, h, stride);
                }
            }

            // Render
            ctx.OMSetRenderTargets(rtv);

            //Resize viewport
            ctx.RSSetViewport(new Viewport(
                                        0,
                                        0,
                                        _bbW,
                                        _bbH,
                                        0.0f,
                                        1.0f));

            // Green fallback if no video yet
            if (!_hasVideo || _videoTex == null)
            {
                ctx.ClearRenderTargetView(rtv, new Color4(0.0f, 0.35f, 0.0f, 1.0f));
                sc.Present(1, PresentFlags.None);
                return;
            }

            // Copy video to backbuffer (no scaling here; we’ll add quad/shader later)
            ctx.OMSetRenderTargets(rtv);
            ctx.RSSetViewport(new Viewport(0, 0, _bbW, _bbH, 0.0f, 1.0f));

            ctx.ClearRenderTargetView(rtv, new Color4(0, 0, 0, 1));

            if (!_hasVideo || _videoSrv == null || _vs == null || _psCrt == null || _vertexBuffer == null || _inputLayout == null || _sampler == null)
            {
                sc.Present(1, PresentFlags.None);
                return;
            }

            // --- ALWAYS upload CRT params (real-time sliders depend on this) ---
            if (_crtCBuffer != null)
            {
                _crt.ScreenWidth = _bbW;
                _crt.ScreenHeight = _bbH;
                _crt.EffectiveWidth = _bbW;
                _crt.EffectiveHeight = _bbH;

                var mapped = ctx.Map(_crtCBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    Marshal.StructureToPtr(_crt, mapped.DataPointer, false);
                }
                finally
                {
                    ctx.Unmap(_crtCBuffer, 0);
                }
            }
            // Pipeline
            ctx.IASetInputLayout(_inputLayout);
            ctx.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleStrip);
            ctx.IASetVertexBuffer(0, _vertexBuffer, stride: sizeof(float) * 4, offset: 0);

            ctx.VSSetShader(_vs);
            ctx.PSSetShader(_psCrt);
            ctx.PSSetSampler(0, _sampler);
            ctx.PSSetShaderResource(0, _videoSrv);
            ctx.PSSetConstantBuffer(0, _crtCBuffer);
            //CRT Pixel Shader setup
            // Fill size terms from current backbuffer
            _crt.ScreenWidth = _bbW;
            _crt.ScreenHeight = _bbH;

            // For now, geometry not ported: Effective = Screen
            _crt.EffectiveWidth = _bbW;
            _crt.EffectiveHeight = _bbH;

            if (_crtCBuffer != null)
            {
                var mapped = ctx.Map(_crtCBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    System.Runtime.InteropServices.Marshal.StructureToPtr(_crt, mapped.DataPointer, false);
                }
                finally
                {
                    ctx.Unmap(_crtCBuffer, 0);
                }

                ctx.PSSetConstantBuffer(0, _crtCBuffer);
            }


            // Draw fullscreen quad
            ctx.Draw(4, 0);

            // Unbind SRV (avoids warnings if you ever render into same texture later)
            ctx.PSSetShaderResource(0, null);

            sc.Present(1, PresentFlags.None);


            _lastPresentTicks = Stopwatch.GetTimestamp();
        }

        private void UploadFrameOnRenderThread(byte[] bgra, int width, int height, int stride)
        {
            ID3D11Device? dev;
            ID3D11DeviceContext? ctx;

            lock (_d3dLock)
            {
                dev = _device;
                ctx = _context;
            }

            if (dev == null || ctx == null) return;

            // Create/recreate textures if size changed
            if (_videoTex == null || _stagingTex == null || width != _videoW || height != _videoH)
            {
                _videoTex?.Dispose();
                _stagingTex?.Dispose();

                _videoW = width;
                _videoH = height;

                _videoTex = dev.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource,      // <-- IMPORTANT
                    CPUAccessFlags = CpuAccessFlags.None,
                    MiscFlags = ResourceOptionFlags.None
                });

                _videoSrv?.Dispose();
                _videoSrv = dev.CreateShaderResourceView(_videoTex); // <-- IMPORTANT

                _stagingTex = dev.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Write,
                    MiscFlags = ResourceOptionFlags.None
                });
            }

            // Map staging (safe here: render thread owns the immediate context usage)
            var mapped = ctx.Map(_stagingTex!, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);

            try
            {
                unsafe
                {
                    fixed (byte* srcBase = bgra)
                    {
                        byte* dstBase = (byte*)mapped.DataPointer;
                        int dstPitch = (int)mapped.RowPitch;
                        int copyBytes = Math.Min(stride, dstPitch);

                        for (int y = 0; y < height; y++)
                        {
                            Buffer.MemoryCopy(
                                srcBase + (y * stride),
                                dstBase + (y * dstPitch),
                                dstPitch,
                                copyBytes);
                        }
                    }
                }
            }
            finally
            {
                ctx.Unmap(_stagingTex!, 0);
            }

            // GPU-side async copy
            ctx.CopyResource(_videoTex!, _stagingTex!);

            _hasVideo = true;
        }

        private void CreateOrUpdateRtv()
        {
            if (_swapChain == null || _device == null) return;

            using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
            _rtv?.Dispose();
            _rtv = _device.CreateRenderTargetView(backBuffer);
        }

        public void SetCrtParams(
            float brightness,
            float contrast,
            float saturation,
            float gamma,
            float scanlines,
            float phosphor,
            float scanlinePhase,
            float maskType,
            float beamWidth,
            float hSize,
            float vSize)
        {
            // Don’t touch the D3D context here (wrong thread risk).
            // Just update the struct; render thread will upload it.
            _crt.Brightness = brightness;
            _crt.Contrast = contrast;
            _crt.Saturation = saturation;
            _crt.Gamma = gamma;
            _crt.ScanlineStrength = scanlines;
            _crt.PhosphorStrength = phosphor;
            _crt.ScanlinePhase = scanlinePhase;
            _crt.MaskType = maskType;
            _crt.BeamWidth = beamWidth;
            _crt.HSize = hSize;
            _crt.VSize = vSize;
        }



        public void Dispose()
        {
            Stop();

            lock (_d3dLock)
            {
                _stagingTex?.Dispose(); _stagingTex = null;
                _videoTex?.Dispose(); _videoTex = null;

                _rtv?.Dispose(); _rtv = null;
                _swapChain?.Dispose(); _swapChain = null;

                _context?.Dispose(); _context = null;
                _device?.Dispose(); _device = null;
            }
        }
    }
}
