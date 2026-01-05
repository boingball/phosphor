using System;
using System.Diagnostics;
using System.Threading;
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

                CreateOrUpdateRtv();
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

                if (width == _bbW && height == _bbH) return;

                _rtv?.Dispose();
                _rtv = null;

                _swapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None);
                _bbW = width;
                _bbH = height;

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

            // Green fallback if no video yet
            if (!_hasVideo || _videoTex == null)
            {
                ctx.ClearRenderTargetView(rtv, new Color4(0.0f, 0.35f, 0.0f, 1.0f));
                sc.Present(1, PresentFlags.None);
                return;
            }

            // Copy video to backbuffer (no scaling here; we’ll add quad/shader later)
            using var backBuffer = sc.GetBuffer<ID3D11Texture2D>(0);

            // Avoid invalid argument if video larger than backbuffer
            var bbDesc = backBuffer.Description;
            int copyW = Math.Min(_videoW, (int)bbDesc.Width);
            int copyH = Math.Min(_videoH, (int)bbDesc.Height);

            if (copyW <= 0 || copyH <= 0)
            {
                ctx.ClearRenderTargetView(rtv, new Color4(0, 0, 0, 1));
                sc.Present(1, PresentFlags.None);
                return;
            }

            var srcBox = new Box(0, 0, 0, copyW, copyH, 1);
            ctx.CopySubresourceRegion(backBuffer, 0, 0, 0, 0, _videoTex, 0, srcBox);

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
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.None,
                    MiscFlags = ResourceOptionFlags.None
                });

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
