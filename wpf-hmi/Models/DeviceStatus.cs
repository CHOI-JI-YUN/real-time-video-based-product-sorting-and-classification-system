using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;

namespace VisiPickHMI.Models
{
    public class DeviceStatus : INotifyPropertyChanged
    {
        private static readonly List<DeviceStatus> _instances = new();
        private static readonly DispatcherTimer _syncBlinkTimer;
        private static bool _blinkOn = true;

        static DeviceStatus()
        {
            _syncBlinkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            _syncBlinkTimer.Tick += (_, _) =>
            {
                _blinkOn = !_blinkOn;
                foreach (var device in _instances.ToArray())
                    device.ApplyCurrentColor();
            };
            _syncBlinkTimer.Start();
        }

        public DeviceStatus()
        {
            _instances.Add(this);
            ApplyCurrentColor();
        }

        private string _name = string.Empty;
        private string _state = "Disconnected";
        private SolidColorBrush _statusColor = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
        private DateTime _lastHeartbeat = DateTime.MinValue;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string State
        {
            get => _state;
            set
            {
                if (_state == value) return;
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsConnected));
                ApplyCurrentColor();
            }
        }

        public SolidColorBrush StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(); }
        }

        public DateTime LastHeartbeat
        {
            get => _lastHeartbeat;
            set { _lastHeartbeat = value; OnPropertyChanged(); }
        }

        public bool IsConnected => State switch
        {
            "Disconnected" => false,
            "Error" => false,
            _ => true
        };

        private Color BaseColor => State switch
        {
            "Connected" => Color.FromRgb(0x00, 0xE6, 0x76),
            "Ready" => Color.FromRgb(0x00, 0xE6, 0x76),
            "Moving" => Color.FromRgb(0x00, 0xE6, 0x76),
            "Waiting" => Color.FromRgb(0x00, 0xE6, 0x76),
            "Idle" => Color.FromRgb(0x00, 0xE6, 0x76),
            "Disconnected" => Color.FromRgb(0xFF, 0x3B, 0x30),
            "Error" => Color.FromRgb(0xFF, 0x3B, 0x30),
            "Maintenance" => Color.FromRgb(0x8E, 0x8E, 0x93),
            _ => Color.FromRgb(0xFF, 0x3B, 0x30)
        };

        private void ApplyCurrentColor()
        {
            var color = BaseColor;

            // 연결된 장비만 전체가 같은 타이밍으로 천천히 깜빡임.
            // 끊긴 장비는 빨강 고정.
            if (IsConnected && !_blinkOn)
                StatusColor = new SolidColorBrush(Color.FromArgb(0x45, color.R, color.G, color.B));
            else
                StatusColor = new SolidColorBrush(color);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
