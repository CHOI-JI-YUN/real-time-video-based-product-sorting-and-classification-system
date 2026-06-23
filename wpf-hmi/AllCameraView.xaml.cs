using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MahApps.Metro.Controls;
using VisiPickHMI.Models;
using VisiPickHMI.ViewModels;

namespace VisiPickHMI
{
    public partial class AllCameraView : MetroWindow
    {
        private readonly DashboardViewModel _vm;
        private readonly DeviceStatus _devCam1;
        private readonly DeviceStatus _devCam2;

        private Storyboard? _blinkCam1;
        private Storyboard? _blinkCam2;

        public AllCameraView(DashboardViewModel vm)
        {
            InitializeComponent();
            _vm = vm;

            // Devices 컬렉션에서 Camera1 / Camera2 찾기
            _devCam1 = _vm.Devices.First(d => d.Name == "Camera1");
            _devCam2 = _vm.Devices.First(d => d.Name == "Camera2");

            _blinkCam1 = CreateBlinkStoryboard(DotCam1);
            _blinkCam2 = CreateBlinkStoryboard(DotCam2);

            // DeviceStatus 변경 구독 (메인 상단 바와 동일한 소스)
            _devCam1.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DeviceStatus.State))
                    Dispatcher.InvokeAsync(() => ApplyDeviceState(DotCam1, LiveLabelCam1, _blinkCam1, _devCam1));
            };
            _devCam2.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DeviceStatus.State))
                    Dispatcher.InvokeAsync(() => ApplyDeviceState(DotCam2, LiveLabelCam2, _blinkCam2, _devCam2));
            };

            // CameraFrame 변경 구독 (영상 업데이트)
            _vm.PropertyChanged += Vm_PropertyChanged;

            // 초기 상태 반영
            ApplyDeviceState(DotCam1, LiveLabelCam1, _blinkCam1, _devCam1);
            ApplyDeviceState(DotCam2, LiveLabelCam2, _blinkCam2, _devCam2);
            RefreshCamera1(_vm.CameraFrame1);
            RefreshCamera2(_vm.CameraFrame2);
        }

        // ── Storyboard 생성 ────────────────────────────────────────
        private static Storyboard CreateBlinkStoryboard(Ellipse dot)
        {
            var anim = new DoubleAnimation
            {
                From = 1.0,
                To = 0.25,
                Duration = TimeSpan.FromSeconds(0.5),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(anim, dot);
            Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));

            var sb = new Storyboard();
            sb.Children.Add(anim);
            return sb;
        }

        // ── DeviceStatus.State → dot + 라벨 + 깜빡임 동기화 ────────
        private static void ApplyDeviceState(
            Ellipse dot,
            System.Windows.Controls.TextBlock label,
            Storyboard? blink,
            DeviceStatus dev)
        {
            if (dev.IsConnected)
            {
                dot.Fill   = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                dot.Opacity = 1.0;
                label.Text       = "LIVE";
                label.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                blink?.Begin();
            }
            else
            {
                blink?.Stop();
                dot.Fill    = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
                dot.Opacity = 1.0;
                label.Text       = "OFFLINE";
                label.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
            }
        }

        // ── CameraFrame 영상 업데이트 ─────────────────────────────
        private void RefreshCamera1(BitmapSource? frame)
        {
            Dispatcher.InvokeAsync(() =>
            {
                ImgCamera1.Source = frame;
                PlaceholderCam1.Visibility = frame == null ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        private void RefreshCamera2(BitmapSource? frame)
        {
            Dispatcher.InvokeAsync(() =>
            {
                ImgCamera2.Source = frame;
                PlaceholderCam2.Visibility = frame == null ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(DashboardViewModel.CameraFrame1):
                    RefreshCamera1(_vm.CameraFrame1);
                    break;
                case nameof(DashboardViewModel.CameraFrame2):
                    RefreshCamera2(_vm.CameraFrame2);
                    break;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _blinkCam1?.Stop();
            _blinkCam2?.Stop();
            _vm.PropertyChanged -= Vm_PropertyChanged;
            base.OnClosed(e);
        }
    }
}
