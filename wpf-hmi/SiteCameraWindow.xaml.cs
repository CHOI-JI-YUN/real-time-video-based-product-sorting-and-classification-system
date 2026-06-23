using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MahApps.Metro.Controls;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisiPickHMI.Services;

namespace VisiPickHMI
{
    /// <summary>
    /// 로그인 화면의 "현장 CCTV" 버튼이 띄우는 창.
    /// HMI PC에 직접 꽂힌 USB 웹캠을 OpenCvSharp VideoCapture로 바로 읽어서 보여준다.
    /// </summary>
    public partial class SiteCameraWindow : MetroWindow
    {
        private CancellationTokenSource? _cts;
        private int _deviceIndex;

        // ── 줌 ──
        private double _zoom = 1.0;
        private const double ZoomMin = 1.0;
        private const double ZoomMax = 5.0;
        private const double ZoomStep = 0.25;

        // ── 팬(드래그 이동) ──
        private bool _isPanning;
        private System.Windows.Point _panStart;

        public SiteCameraWindow()
        {
            InitializeComponent();
            _deviceIndex = Math.Max(0, AppConfig.SiteCameraIndex);

            Loaded += (_, _) => StartCapture(_deviceIndex);
            Closed += (_, _) => StopCapture();
        }

        // ── 캡처 시작 ──────────────────────────────────────────
        private void StartCapture(int deviceIndex)
        {
            StopCapture();
            _deviceIndex = deviceIndex;
            SetState(connected: false, "카메라 연결 중...");

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => CaptureLoop(deviceIndex, token), token);
        }

        private void StopCapture()
        {
            _cts?.Cancel();
            _cts = null;
        }

        // ── 캡처 루프 (백그라운드 스레드) ──────────────────────
        private void CaptureLoop(int deviceIndex, CancellationToken token)
        {
            VideoCapture? cap = null;
            try
            {
                cap = new VideoCapture(deviceIndex, VideoCaptureAPIs.DSHOW);
                if (!cap.IsOpened())
                {
                    cap.Dispose();
                    cap = new VideoCapture(deviceIndex);
                }

                if (!cap.IsOpened())
                {
                    UpdateUi(() => SetState(false,
                        $"CAM {deviceIndex} 를 열 수 없습니다.\nappsettings.json 의 DeviceIndex를 확인해주세요."));
                    return;
                }

                using var frame = new Mat();
                bool wentLive = false;

                while (!token.IsCancellationRequested)
                {
                    if (!cap.Read(frame) || frame.Empty())
                    {
                        Thread.Sleep(30);
                        continue;
                    }

                    BitmapSource bmp = frame.ToBitmapSource();
                    bmp.Freeze();

                    if (!wentLive)
                    {
                        wentLive = true;
                        UpdateUi(() => SetState(true, null));
                    }

                    UpdateUi(() =>
                    {
                        ImgSite.Source = bmp;
                        PlaceholderSite.Visibility = Visibility.Collapsed;
                    });

                    Thread.Sleep(33);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UpdateUi(() => SetState(false, $"카메라 오류: {ex.Message}"));
            }
            finally
            {
                cap?.Release();
                cap?.Dispose();
            }
        }

        // ── LIVE / OFFLINE 표시 ────────────────────────────────
        private void SetState(bool connected, string? placeholderMsg)
        {
            if (connected)
            {
                DotLive.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                LiveLabel.Text = "LIVE";
                LiveLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
            }
            else
            {
                DotLive.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
                LiveLabel.Text = "OFFLINE";
                LiveLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
                ImgSite.Source = null;
                PlaceholderSite.Visibility = Visibility.Visible;
                if (placeholderMsg != null)
                    TxtPlaceholder.Text = placeholderMsg;
            }
        }

        // ── 줌 컨트롤 ─────────────────────────────────────────
        private void ApplyZoom(double newZoom)
        {
            _zoom = Math.Clamp(newZoom, ZoomMin, ZoomMax);
            ImageScale.ScaleX = _zoom;
            ImageScale.ScaleY = _zoom;
            TxtZoomLevel.Text = $"{(int)(_zoom * 100)}%";
            if (_zoom <= 1.0)
            {
                ImageTranslate.X = 0;
                ImageTranslate.Y = 0;
            }
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => ApplyZoom(_zoom + ZoomStep);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => ApplyZoom(_zoom - ZoomStep);
        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            ApplyZoom(1.0);
            ImageTranslate.X = 0;
            ImageTranslate.Y = 0;
        }

        // ── 팬(드래그 이동) ──────────────────────────────────
        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_zoom <= 1.0) return;
            _isPanning = true;
            _panStart = e.GetPosition(ImageContainer);
            ImageContainer.Cursor = Cursors.SizeAll;
            ImageContainer.CaptureMouse();
            e.Handled = true;
        }

        private void Image_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning) return;
            var pos = e.GetPosition(ImageContainer);
            ImageTranslate.X += pos.X - _panStart.X;
            ImageTranslate.Y += pos.Y - _panStart.Y;
            _panStart = pos;
        }

        private void Image_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isPanning) return;
            _isPanning = false;
            ImageContainer.Cursor = Cursors.Arrow;
            ImageContainer.ReleaseMouseCapture();
        }

        /// <summary>마우스 휠로 줌 (Ctrl+휠)</summary>
        private void ImgSite_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                double delta = e.Delta > 0 ? ZoomStep : -ZoomStep;
                ApplyZoom(_zoom + delta);
                e.Handled = true;
            }
        }

        private void UpdateUi(Action action)
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.InvokeAsync(action);
        }
    }
}
