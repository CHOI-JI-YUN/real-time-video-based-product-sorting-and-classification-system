using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VisiPickHMI.Models
{
    public class ClassificationSummary : INotifyPropertyChanged
    {
        private int _totalCount;
        private int _passCount;
        private int _defectCount;
        private double _yieldRate;
        private int _classACount;
        private int _classBCount;
        private int _classCCount;
        private int _classDCount;

        public int TotalCount
        {
            get => _totalCount;
            set { _totalCount = value; OnPropertyChanged(); RecalcYield(); }
        }

        public int PassCount
        {
            get => _passCount;
            set { _passCount = value; OnPropertyChanged(); RecalcYield(); }
        }

        public int DefectCount
        {
            get => _defectCount;
            set { _defectCount = value; OnPropertyChanged(); }
        }

        public double YieldRate
        {
            get => _yieldRate;
            set { _yieldRate = value; OnPropertyChanged(); OnPropertyChanged(nameof(YieldRateText)); }
        }

        public string YieldRateText => _yieldRate.ToString("F1");

        // A: IC칩
        public int ClassACount
        {
            get => _classACount;
            set { _classACount = value; OnPropertyChanged(); }
        }

        // B: 터미널 블록
        public int ClassBCount
        {
            get => _classBCount;
            set { _classBCount = value; OnPropertyChanged(); }
        }

        // C: 방열판
        public int ClassCCount
        {
            get => _classCCount;
            set { _classCCount = value; OnPropertyChanged(); }
        }

        // D: 커패시터
        public int ClassDCount
        {
            get => _classDCount;
            set { _classDCount = value; OnPropertyChanged(); }
        }

        private void RecalcYield()
        {
            YieldRate = TotalCount > 0 ? Math.Round((double)PassCount / TotalCount * 100, 1) : 0;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}