using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace VisiPickHMI.Models
{
    public class TraySlot : INotifyPropertyChanged
    {
        // ── 부품 실루엣 Path Data (32×32 ViewBox 기준) ──

        // A: DIP IC (직사각 본체 + 좌우 핀 + 인덱스 홈)
        public static string PathA =
            "M8,4 L24,4 L24,28 L8,28 Z " +
            "M4,7 L8,7 L8,10 L4,10 Z " +
            "M4,13 L8,13 L8,16 L4,16 Z " +
            "M4,19 L8,19 L8,22 L4,22 Z " +
            "M4,25 L8,25 L8,28 L4,28 Z " +
            "M24,7 L28,7 L28,10 L24,10 Z " +
            "M24,13 L28,13 L28,16 L24,16 Z " +
            "M24,17 L28,17 L28,20 L24,20 Z " +
            "M24,23 L28,23 L28,26 L24,26 Z " +
            "M13,2 A3,3 0 0,0 13,8";

        // B: 터미널 블록 (사각 본체 + 상단 구멍들)
        public static string PathB =
            "M2,10 L30,10 L30,30 L2,30 Z " +
            "M6,4 L6,10 M6,7 A3,3 0 1,1 6.01,7 " +
            "M16,4 L16,10 M16,7 A3,3 0 1,1 16.01,7 " +
            "M26,4 L26,10 M26,7 A3,3 0 1,1 26.01,7 " +
            "M2,20 L30,20";

        // C: 방열판 (핀이 있는 직사각형)
        public static string PathC =
            "M2,28 L2,4 L6,4 L6,2 L10,2 L10,4 " +
            "L14,4 L14,2 L18,2 L18,4 " +
            "L22,4 L22,2 L26,2 L26,4 " +
            "L30,4 L30,28 Z " +
            "M6,10 L6,24 M10,8 L10,24 " +
            "M14,10 L14,24 M18,8 L18,24 " +
            "M22,10 L22,24 M26,8 L26,24";

        // D: 커패시터/원통 (타원+다리)
        public static string PathD =
            "M16,4 A10,10 0 1,1 16.01,4 " +
            "M16,4 A10,4 0 1,0 16.01,4 " +
            "M6,14 A10,4 0 1,1 26,14 " +
            "M10,24 L10,30 M22,24 L22,30 " +
            "M12,2 L12,0 L14,0 L14,2";

        // ── 색상 상수 ──
        private static SolidColorBrush ColorA => Freeze("#3360A5FA");
        private static SolidColorBrush ColorB => Freeze("#3334D399");
        private static SolidColorBrush ColorC => Freeze("#33FBBF24");
        private static SolidColorBrush ColorD => Freeze("#33A78BFA");
        private static SolidColorBrush ColorEmpty => Freeze("#11FFFFFF");

        private static SolidColorBrush StrokeA => Freeze("#60A5FA");
        private static SolidColorBrush StrokeB => Freeze("#34D399");
        private static SolidColorBrush StrokeC => Freeze("#FCD34D");
        private static SolidColorBrush StrokeD => Freeze("#C4B5FD");
        private static SolidColorBrush StrokeEmpty => Freeze("#33FFFFFF");

        // ── 백킹 필드 ──
        private string _slotId = "";
        private string _label = "";
        private bool _isFilled;
        private string _componentClass = "";

        // ── 프로퍼티 ──

        public string SlotId
        {
            get => _slotId;
            set
            {
                _slotId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SlotPath));
                OnPropertyChanged(nameof(SlotName));
                OnPropertyChanged(nameof(SlotFill));
                OnPropertyChanged(nameof(SlotStroke));
                OnPropertyChanged(nameof(SlotBackground));
            }
        }

        public string Label
        {
            get => _label;
            set { _label = value; OnPropertyChanged(); }
        }

        public bool IsFilled
        {
            get => _isFilled;
            set
            {
                _isFilled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SlotBackground));
                OnPropertyChanged(nameof(SlotBorderBrush));
                OnPropertyChanged(nameof(SlotOpacity));
                OnPropertyChanged(nameof(SlotStroke));
                OnPropertyChanged(nameof(SlotFill));
            }
        }

        public string ComponentClass
        {
            get => _componentClass;
            set
            {
                _componentClass = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SlotBackground));
                OnPropertyChanged(nameof(SlotStroke));
                OnPropertyChanged(nameof(SlotFill));
            }
        }

        // ── 바인딩용 속성들 ──

        public string SlotPath => SlotId switch
        {
            "A" => PathA, "B" => PathB, "C" => PathC, "D" => PathD, _ => PathA
        };

        public string SlotName => SlotId switch
        {
            "A" => "IC칩", "B" => "터미널", "C" => "방열판", "D" => "커패시터", _ => ""
        };

        public SolidColorBrush SlotFill => IsFilled
            ? SlotId switch { "A" => ColorA, "B" => ColorB, "C" => ColorC, "D" => ColorD, _ => ColorA }
            : Freeze("Transparent");

        public SolidColorBrush SlotStroke => IsFilled
            ? SlotId switch { "A" => StrokeA, "B" => StrokeB, "C" => StrokeC, "D" => StrokeD, _ => StrokeA }
            : StrokeEmpty;

        public SolidColorBrush SlotBackground => IsFilled
            ? SlotId switch { "A" => ColorA, "B" => ColorB, "C" => ColorC, "D" => ColorD, _ => ColorA }
            : ColorEmpty;

        public SolidColorBrush SlotBorderBrush => IsFilled ? Freeze("#44FFFFFF") : Freeze("#22FFFFFF");
        public double SlotOpacity => IsFilled ? 1.0 : 0.5;

        // ── 메서드 ──
        public void Fill(string componentClass) { ComponentClass = componentClass; IsFilled = true; }
        public void Clear() { ComponentClass = ""; IsFilled = false; }

        // ── 헬퍼 ──
        private static SolidColorBrush Freeze(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
