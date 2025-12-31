using AForge.Video.DirectShow;
using DirectShowLib;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DsFilterCategory = DirectShowLib.FilterCategory;

namespace RetroDisplay
{
    public partial class MainWindow : Window
    {
        private VideoCaptureDevice? videoDevice;
        private FilterInfoCollection? videoDevices;
        private List<VideoCapabilities>? currentCapabilities;
        private List<string> audioDevices = new List<string>();
        private bool isCapturing = false;
        private readonly RetroCrtEffect crtEffect = new();
        private ScaleTransform scaleTransform;
        private TranslateTransform translateTransform;

        // === AUDIO ===
        private WasapiCapture? audioCapture;
        private WasapiOut? audioOutput;
        private BufferedWaveProvider? audioBuffer;
        private VolumeSampleProvider? volumeSampleProvider;

        private MMDeviceEnumerator? audioEnumerator;
        private List<MMDevice> audioInputDevices = new();
        private List<MMDevice> audioOutputDevices = new();




        public MainWindow()
        {
            InitializeComponent();
            VideoPlayer.Effect = crtEffect;
            LoadSettings();
            InitCrtGeometry();
            InitializeDevices();
            InitializeAudioDevices(); // audio (NEW)
        }

        private void LoadSettings()
        {
            BrightnessSlider.Value = Properties.Settings.Default.Brightness;
            ContrastSlider.Value = Properties.Settings.Default.Contrast;
            SaturationSlider.Value = Properties.Settings.Default.Saturation;
            GammaSlider.Value = Properties.Settings.Default.Gamma;
            PhosphorSlider.Value = Properties.Settings.Default.Phosphor;
            ScanlinesSlider.Value = Properties.Settings.Default.Scanlines;
            VignetteSlider.Value = Properties.Settings.Default.Vignette;
            HorizontalSlider.Value = Properties.Settings.Default.HSize;
            VerticalSlider.Value = Properties.Settings.Default.VSize;
        }

        private void ApplySettingsToPipeline()
        {
            if (scaleTransform == null)
                return;

            // Effects
            crtEffect.Brightness = BrightnessSlider.Value;
            crtEffect.Contrast = ContrastSlider.Value;
            crtEffect.Saturation = SaturationSlider.Value;
            crtEffect.Gamma = GammaSlider.Value;
            crtEffect.PhosphorStrength = PhosphorSlider.Value;
            crtEffect.ScanlineStrength = ScanlinesSlider.Value;

            if (VignetteOverlay != null)
                VignetteOverlay.Opacity = VignetteSlider.Value;

            // Geometry
            scaleTransform.ScaleX = 1.07 + HorizontalSlider.Value;
            scaleTransform.ScaleY = 1.0 + VerticalSlider.Value;
        }


        private void InitCrtGeometry()
        {
            // Base values that look sane on most SCART boxes
            scaleTransform = new ScaleTransform(1.07, 1.0);
            translateTransform = new TranslateTransform(0, 0);

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(scaleTransform);
            transformGroup.Children.Add(translateTransform);

            VideoPlayer.RenderTransformOrigin = new Point(0.5, 0.5);
            VideoPlayer.RenderTransform = transformGroup;
        }

        private void InitializeDevices()
        {
            try
            {
                videoDevices = new FilterInfoCollection(AForge.Video.DirectShow.FilterCategory.VideoInputDevice);

                VideoSourceCombo.Items.Clear();
                VideoSourceCombo.Items.Add("-- Select Video Source --");

                foreach (AForge.Video.DirectShow.FilterInfo device in videoDevices)
                {
                    VideoSourceCombo.Items.Add(device.Name);
                }

                VideoSourceCombo.SelectedIndex = 0;

                //Restore saved video device selection
                if (!string.IsNullOrEmpty(Properties.Settings.Default.VideoSource))
                {
                    VideoSourceCombo.SelectedItem =
                        Properties.Settings.Default.VideoSource;
                }

                VideoModeCombo.SelectedIndex =
                    Properties.Settings.Default.VideoMode;

                audioDevices = new List<string>();
                AudioSourceCombo.Items.Clear();
                AudioSourceCombo.Items.Add("-- Select Audio Source --");

                DsDevice[] audioInputDevices = DsDevice.GetDevicesOfCat(DsFilterCategory.AudioInputDevice);

                foreach (DsDevice device in audioInputDevices)
                {
                    audioDevices.Add(device.Name);
                    AudioSourceCombo.Items.Add(device.Name);
                }

                AudioSourceCombo.SelectedIndex = 0;

                StatusText.Text = $"Found {videoDevices.Count} video device(s)";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Failed to enumerate devices: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private VideoCapabilities SelectBestCapability(VideoCaptureDevice device)
        {
            VideoCapabilities best = device.VideoCapabilities[0];

            foreach (var cap in device.VideoCapabilities)
            {
                int bestPixels = best.FrameSize.Width * best.FrameSize.Height;
                int capPixels = cap.FrameSize.Width * cap.FrameSize.Height;

                if (capPixels > bestPixels &&
                    cap.AverageFrameRate >= 25)
                {
                    best = cap;
                }
            }

            return best;
        }

        private void InitializeAudioDevices()
        {
            audioEnumerator = new MMDeviceEnumerator();

            AudioSourceCombo.Items.Clear();
            AudioSourceCombo.Items.Add("-- Select Audio Input --");
            audioInputDevices.Clear();

            foreach (var dev in audioEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                audioInputDevices.Add(dev);
                AudioSourceCombo.Items.Add(dev.FriendlyName);
            }

            AudioSourceCombo.SelectedIndex = 0;

            // OPTIONAL: output selector later
        }

        private void VideoSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            VideoModeCombo.Items.Clear();
            VideoModeCombo.IsEnabled = false;

            if (VideoSourceCombo.SelectedIndex <= 0 || videoDevices == null)
                return;

            int deviceIndex = VideoSourceCombo.SelectedIndex - 1;
            var device = new VideoCaptureDevice(videoDevices[deviceIndex].MonikerString);

            currentCapabilities = device.VideoCapabilities
                .OrderByDescending(c => c.AverageFrameRate)
                .ThenByDescending(c => c.FrameSize.Width * c.FrameSize.Height)
                .ToList();

            VideoModeCombo.Items.Add("Auto (best quality)");

            foreach (var cap in currentCapabilities)
            {
                VideoModeCombo.Items.Add(
                    $"{cap.FrameSize.Width}×{cap.FrameSize.Height} @ {cap.AverageFrameRate} fps"
                );
            }

            VideoModeCombo.SelectedIndex = 0;
            VideoModeCombo.IsEnabled = true;
        }

        private void AudioSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isCapturing)
                return;

            // Stop current audio (if any)
            StopAudio();

            // Start new audio if a valid device is selected
            if (AudioSourceCombo.SelectedIndex > 0)
                StartAudio();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (VideoSourceCombo.SelectedIndex <= 0)
            {
                MessageBox.Show("Please select a video source first.", "No Source Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                StopCapture();

                int deviceIndex = VideoSourceCombo.SelectedIndex - 1;
                videoDevice = new VideoCaptureDevice(videoDevices[deviceIndex].MonikerString);

                VideoCapabilities selectedCap;

                if (VideoModeCombo.SelectedIndex <= 0 || currentCapabilities == null)
                {
                    // Auto mode (your existing logic)
                    selectedCap = SelectBestCapability(videoDevice);
                }
                else
                {
                    selectedCap = currentCapabilities[VideoModeCombo.SelectedIndex - 1];
                }

                videoDevice.VideoResolution = selectedCap;

                StatusText.Text =
                    $"{selectedCap.FrameSize.Width}×{selectedCap.FrameSize.Height} @ {selectedCap.AverageFrameRate} fps";

                videoDevice.NewFrame += VideoDevice_NewFrame;
                videoDevice.Start();
                //Apply saved settings to pipeline
                ApplySettingsToPipeline();
                StartAudio();

                //Save settings for Video Device and Mode
                Properties.Settings.Default.VideoSource =
                VideoSourceCombo.SelectedItem?.ToString();

                Properties.Settings.Default.VideoMode =
                    VideoModeCombo.SelectedIndex;

                Properties.Settings.Default.Save();

                isCapturing = true;
                PlaceholderGrid.Visibility = Visibility.Collapsed;
                StatusText.Text = "Capturing...";
                StatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Failed to start capture: {ex.Message}", "Capture Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private WriteableBitmap? writeableBitmap;
        private volatile int framePending = 0; // drop frames if UI is behind
        private int frameCount = 0;
        private DateTime fpsStart = DateTime.Now;

        private void VideoDevice_NewFrame(object sender, AForge.Video.NewFrameEventArgs e)
    {
            int frames = Interlocked.Increment(ref frameCount);

            var elapsed = DateTime.Now - fpsStart;
            if (elapsed.TotalSeconds >= 1)
            {
                int fps = frames;
                Interlocked.Exchange(ref frameCount, 0);
                fpsStart = DateTime.Now;

                Dispatcher.BeginInvoke(() =>
                {
                    StatusText.Text = $"FPS: {fps}";
                });
            }
            // If UI hasn't consumed the last frame yet, drop this one.
            if (Interlocked.Exchange(ref framePending, 1) == 1)
            return;

        int width, height;
        byte[] pixels;

        try
        {
            using var src = (System.Drawing.Bitmap)e.Frame.Clone();

            width = src.Width;
            height = src.Height;

            // Convert to 24bpp safely
            using var frame24 = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = System.Drawing.Graphics.FromImage(frame24))
            {
                    g.DrawImage(src, 0, 0, width, height);
            }

            var rect = new System.Drawing.Rectangle(0, 0, width, height);
            var data = frame24.LockBits(rect,
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            try
            {
                int srcStride = data.Stride;
                int dstStride = width * 3;

                pixels = new byte[dstStride * height];

                // Handle negative stride (bottom-up bitmap)
                if (srcStride > 0)
                {
                    for (int y = 0; y < height; y++)
                    {
                        IntPtr srcRow = IntPtr.Add(data.Scan0, y * srcStride);
                        Buffer.BlockCopy(MarshalRow(srcRow, dstStride), 0, pixels, y * dstStride, dstStride);
                    }
                }
                else
                {
                    // Start from last row, walk upward
                    int absStride = -srcStride;
                    IntPtr basePtr = IntPtr.Add(data.Scan0, (height - 1) * absStride);

                    for (int y = 0; y < height; y++)
                    {
                        IntPtr srcRow = IntPtr.Add(basePtr, -y * absStride);
                        Buffer.BlockCopy(MarshalRow(srcRow, dstStride), 0, pixels, y * dstStride, dstStride);
                    }
                }
            }
            finally
            {
                    frame24.UnlockBits(data);
            }
        }
        catch (Exception ex)
        {
            // Release framePending so it doesn't deadlock capture
            Interlocked.Exchange(ref framePending, 0);

            Dispatcher.BeginInvoke(() =>
            {
                StatusText.Text = $"Frame error: {ex.GetType().Name} - {ex.Message}";
            });

            return;
        }

        // Now only WPF work on UI thread
        Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
        {
            try
            {
                if (writeableBitmap == null ||
                    writeableBitmap.PixelWidth != width ||
                    writeableBitmap.PixelHeight != height)
                {
                    writeableBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
                    VideoPlayer.Source = writeableBitmap;
                }

                writeableBitmap.WritePixels(
                    new Int32Rect(0, 0, width, height),
                    pixels,
                    width * 3,
                    0
                );
            }
            finally
            {

                Interlocked.Exchange(ref framePending, 0);
            }
        }));
    }

        // Helper: copies one row from unmanaged memory into a managed byte[]
        private static byte[] MarshalRow(IntPtr src, int count)
    {
        var row = new byte[count];
        Marshal.Copy(src, row, 0, count);
        return row;
    }

        private void StartAudio()
        {
            if (AudioSourceCombo.SelectedIndex <= 0)
                return;

            StopAudio();

            var inputDevice = audioInputDevices[AudioSourceCombo.SelectedIndex - 1];

            // Capture from selected mic/input
            audioCapture = new WasapiCapture(inputDevice);

            audioBuffer = new BufferedWaveProvider(audioCapture.WaveFormat)
            {
                DiscardOnBufferOverflow = true
            };

            // Convert buffer -> samples -> volume control (works with any format)
            var sampleProvider = audioBuffer.ToSampleProvider();
            volumeSampleProvider = new VolumeSampleProvider(sampleProvider)
            {
                Volume = 1.0f
            };

            // Output to default speakers
            var outputDevice = audioEnumerator!.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            audioOutput = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 20);

            audioOutput.Init(volumeSampleProvider);

            audioCapture.DataAvailable += (s, e) =>
            {
                audioBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            };

            audioCapture.RecordingStopped += (s, e) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    StatusText.Text = e.Exception != null
                        ? $"Audio stopped: {e.Exception.Message}"
                        : "Audio stopped";
                });
            };

            audioOutput.Play();
            audioCapture.StartRecording();
        }

        private void StopAudio()
        {
            try
            {
                if (audioCapture != null)
                {
                    audioCapture.StopRecording();
                    audioCapture.Dispose();
                    audioCapture = null;
                }
            }
            catch { /* ignore */ }

            try
            {
                if (audioOutput != null)
                {
                    audioOutput.Stop();
                    audioOutput.Dispose();
                    audioOutput = null;
                }
            }
            catch { /* ignore */ }

            audioBuffer = null;
            volumeSampleProvider = null;
        }



        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
        }

        private void StopCapture()
        {
            if (videoDevice != null && videoDevice.IsRunning)
            {
                videoDevice.SignalToStop();
                videoDevice.WaitForStop();
                videoDevice.NewFrame -= VideoDevice_NewFrame;
                videoDevice = null;
            }
            StopAudio();
            isCapturing = false;
            PlaceholderGrid.Visibility = Visibility.Visible;
            StatusText.Text = "Stopped";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (volumeSampleProvider != null)
                volumeSampleProvider.Volume = (float)e.NewValue;
        }



        private void HSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (scaleTransform == null) return;

            // 1.07 = PAL pixel aspect baseline
            // Slider adds/subtracts from that
            scaleTransform.ScaleX = 1.07 + e.NewValue;
        }

        private void VSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (scaleTransform == null) return;

            // 1.0 = authentic CRT baseline
            // Increase only if SCART box letterboxes
            scaleTransform.ScaleY = 1.0 + e.NewValue;
        }


        private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            crtEffect.Brightness = e.NewValue;
        }

        private void ContrastSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            crtEffect.Contrast = e.NewValue;
        }

        private void SaturationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            crtEffect.Saturation = e.NewValue;
        }

        private void ScanlinesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            crtEffect.ScanlineStrength = e.NewValue;
        }

        private void GammaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            crtEffect.Gamma = e.NewValue;
        }

        private void PhosphorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            crtEffect.PhosphorStrength = e.NewValue;
        }

        private void VignetteSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VignetteOverlay != null)
            {
                VignetteOverlay.Opacity = VignetteSlider.Value;
            }
        }

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == System.Windows.WindowState.Normal)
            {
                this.WindowState = System.Windows.WindowState.Maximized;
                this.WindowStyle = System.Windows.WindowStyle.None;
            }
            else
            {
                this.WindowState = System.Windows.WindowState.Normal;
                this.WindowStyle = System.Windows.WindowStyle.SingleBorderWindow;
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            BrightnessSlider.Value = 0.0;
            ContrastSlider.Value = 1.1;
            SaturationSlider.Value = 1.1;
            GammaSlider.Value = 1.15;
            ScanlinesSlider.Value = 0.20;
            PhosphorSlider.Value = 0.12;
            VignetteSlider.Value = 0.0;

            Properties.Settings.Default.Reset();
            StatusText.Text = "CRT defaults restored";
        }

        private void ResetGeometry()
        {
            HorizontalSlider.Value = 0.0;
            VerticalSlider.Value = 0.0;

            scaleTransform.ScaleX = 1.07;
            scaleTransform.ScaleY = 1.0;

            translateTransform.X = 0;
            translateTransform.Y = 0;
        }

        private void SaveSettings()
{
    Properties.Settings.Default.Brightness = BrightnessSlider.Value;
    Properties.Settings.Default.Contrast   = ContrastSlider.Value;
    Properties.Settings.Default.Saturation = SaturationSlider.Value;
    Properties.Settings.Default.Gamma      = GammaSlider.Value;
    Properties.Settings.Default.Phosphor   = PhosphorSlider.Value;
    Properties.Settings.Default.Scanlines  = ScanlinesSlider.Value;
    Properties.Settings.Default.Vignette   = VignetteSlider.Value;
    Properties.Settings.Default.HSize = HorizontalSlider.Value;
    Properties.Settings.Default.VSize = VerticalSlider.Value;

    Properties.Settings.Default.Save();
}


        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveSettings();
            StopCapture();
            base.OnClosing(e);
        }

        private void ResetGeo_ButtonClick(object sender, RoutedEventArgs e)
        {
            ResetGeometry();
        }
    }
}