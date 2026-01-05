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
        private IntPtr _hwnd;
        private int _width;
        private int _height;

        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGISwapChain1? _swapChain;
        private ID3D11RenderTargetView? _rtv;

        private Thread? _renderThread;
        private volatile bool _running;

        // === VIDEO TEXTURE ===
        private ID3D11Texture2D? _videoTex;
        private int _videoW, _videoH;

        // Locks:
        // - _d3dLock protects _context/_swapChain usage between render thread + UI upload thread
        // - _videoLock protects video texture lifetime/size vars
        private readonly object _d3dLock = new();
        private readonly object _videoLock = new();

        // Simple flag so we stop drawing green once a frame has landed
        private volatile bool _hasVideo;

        public void Initialize(IntPtr hwnd, int width, int height)
        {
            _hwnd = hwnd;
            _width = Math.Max(1, width);
            _height = Math.Max(1, height);

            var flags = DeviceCreationFlags.BgraSupport;
#if DEBUG
            flags |= DeviceCreationFlags.Debug;
#endif
            D3D11CreateDevice(
                null,
                DriverType.Hardware,
                flags,
                null,
                out _device,
                out _context);

            // Enable multithread protection (important if you touch the immediate context from 2 threads)
            try
            {
                using var mt = _context!.QueryInterface<ID3D11Multithread>();
                mt.SetMultithreadProtected(true);
            }
            catch
            {
                // If unavailable for some reason, our _d3dLock still protects usage.
            }

            using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory2>();

            var scDesc = new SwapChainDescription1()
            {
                Width = (uint)_width,
                Height = (uint)_height,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.FlipDiscard,
                Scaling = Scaling.Stretch,
                AlphaMode = AlphaMode.Ignore
            };

            _swapChain = factory.CreateSwapChainForHwnd(_device, _hwnd, scDesc);

            CreateRenderTarget();
        }

        private void CreateRenderTarget()
        {
            _rtv?.Dispose();
            _rtv = null;

            using var backBuffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0);
            _rtv = _device!.CreateRenderTargetView(backBuffer);
        }

        public void Resize(int width, int height)
        {
            if (_swapChain == null) return;

            lock (_d3dLock)
            {
                _width = Math.Max(1, width);
                _height = Math.Max(1, height);

                _rtv?.Dispose();
                _rtv = null;

                _swapChain.ResizeBuffers(0, (uint)_width, (uint)_height, Format.Unknown, SwapChainFlags.None);
                CreateRenderTarget();
            }
        }

        /// <summary>
        /// Upload a BGRA32 frame (DXGI_FORMAT_B8G8R8A8_UNorm) into a dynamic texture.
        /// Safe to call from UI thread.
        /// </summary>
        public void UpdateVideoFrameBgra32(byte[] bgra, int width, int height, int stride)
        {
            if (_device == null || _context == null) return;
            if (bgra == null || bgra.Length == 0) return;
            if (width <= 0 || height <= 0 || stride <= 0) return;


            lock (_d3dLock)
            {
                // Create / resize the video texture as needed
                lock (_videoLock)
                {
                    if (_videoTex == null || width != _videoW || height != _videoH)
                    {
                        _videoTex?.Dispose();
                        _videoTex = null;

                        _videoW = width;
                        _videoH = height;

                        var desc = new Texture2DDescription
                        {
                            Width = (uint)width,
                            Height = (uint)height,
                            MipLevels = 1,
                            ArraySize = 1,
                            Format = Format.B8G8R8A8_UNorm,
                            SampleDescription = new SampleDescription(1, 0),
                            Usage = ResourceUsage.Default,        // 🔥 NOT Dynamic
                            BindFlags = BindFlags.None,
                            CPUAccessFlags = CpuAccessFlags.None, // 🔥 No Map()
                            MiscFlags = ResourceOptionFlags.None
                        };

                        _videoTex = _device.CreateTexture2D(desc);
                    }
                }

                // Upload the entire frame in one call (legal for DEFAULT usage)
                _context.UpdateSubresource(
                    bgra,
                    _videoTex!,
                    0,
                    (uint)stride,
                    0);

                _hasVideo = true;

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

        private void RenderLoop()
        {
            var sw = Stopwatch.StartNew();
            while (_running)
            {
                RenderFrame(sw.ElapsedMilliseconds);
                // Present(1) blocks to vsync, so we won't spin hard
            }
        }

        private void RenderFrame(long ms)
        {
            if (_context == null || _swapChain == null || _rtv == null)
                return;

            if (_videoW > _width || _videoH > _height)
                return;

            lock (_d3dLock)
            {
                _context.OMSetRenderTargets(_rtv, null);

                if (_hasVideo && _videoTex != null)
                {
                    using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);

                    var bbDesc = backBuffer.Description;

                    int copyW = Math.Min(_videoW, (int)bbDesc.Width);
                    int copyH = Math.Min(_videoH, (int)bbDesc.Height);

                    var box = new Box(0, 0, 0, copyW, copyH, 1);

                    // Copy the video texture into the backbuffer (top-left).
                    // (Scaling to fit comes later; first: prove pixels are arriving.)
                    _context.CopySubresourceRegion(
                        backBuffer,
                        0,
                        0, 0, 0,
                        _videoTex,
                        0,
                        null);
                }
                else
                {
                    // Fallback green test so you know render thread is alive
                    float green = 0.15f + 0.25f * ((ms % 2000) / 2000.0f);
                    var clearColor = new Color4(0.05f, green, 0.05f, 1.0f);
                    _context.ClearRenderTargetView(_rtv, clearColor);
                }

                _swapChain.Present(1, PresentFlags.None);
            }
        }

        public void Dispose()
        {
            Stop();

            lock (_d3dLock)
            {
                lock (_videoLock)
                {
                    _videoTex?.Dispose();
                    _videoTex = null;
                }

                _rtv?.Dispose();
                _swapChain?.Dispose();
                _context?.Dispose();
                _device?.Dispose();
            }
        }
    }
}
