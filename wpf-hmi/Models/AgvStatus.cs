using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VisiPickHMI.Models
{
    public class AgvStatus : INotifyPropertyChanged
    {
        private int _agvId;
        private int _node;
        private string _state = "대기";
        private double _positionX;
        private double _positionY;
        private string _destination = string.Empty;
        private string _trayClass = string.Empty;
        private int _itemCount;
        private string _nextAction = string.Empty;
        private int _selectedHome;
        private bool _home1Free = false;
        private bool _home2Free = true;
        private bool _home3Free = true;
        private string _mission = string.Empty;

        public int AgvId
        {
            get => _agvId;
            set { _agvId = value; OnPropertyChanged(); }
        }

        /// <summary>RFID 노드 번호 (이산 위치). 0 = 미확인.</summary>
        public int Node
        {
            get => _node;
            set { _node = value; OnPropertyChanged(); }
        }

        public string State
        {
            get => _state;
            set { _state = value; OnPropertyChanged(); }
        }

        public double PositionX
        {
            get => _positionX;
            set { _positionX = value; OnPropertyChanged(); }
        }

        public double PositionY
        {
            get => _positionY;
            set { _positionY = value; OnPropertyChanged(); }
        }

        public string Destination
        {
            get => _destination;
            set { _destination = value; OnPropertyChanged(); }
        }

        public string TrayClass
        {
            get => _trayClass;
            set { _trayClass = value; OnPropertyChanged(); }
        }

        public int ItemCount
        {
            get => _itemCount;
            set { _itemCount = value; OnPropertyChanged(); }
        }

        /// <summary>ESP32 next_action (예: "ARRIVED_WAREHOUSE_1")</summary>
        public string NextAction
        {
            get => _nextAction;
            set { _nextAction = value; OnPropertyChanged(); }
        }

        /// <summary>선택된 홈 번호 (1/2/3)</summary>
        public int SelectedHome
        {
            get => _selectedHome;
            set { _selectedHome = value; OnPropertyChanged(); }
        }

        public bool Home1Free
        {
            get => _home1Free;
            set { _home1Free = value; OnPropertyChanged(); }
        }

        public bool Home2Free
        {
            get => _home2Free;
            set { _home2Free = value; OnPropertyChanged(); }
        }

        public bool Home3Free
        {
            get => _home3Free;
            set { _home3Free = value; OnPropertyChanged(); }
        }

        public string Mission
        {
            get => _mission;
            set { _mission = value; OnPropertyChanged(); }
        }

        private string _rfidUid = string.Empty;
        /// <summary>현재 인식된 RFID UID (예: "D6:B9:39:F4")</summary>
        public string RfidUid
        {
            get => _rfidUid;
            set { _rfidUid = value; OnPropertyChanged(); }
        }

        private string _returnMode = string.Empty;
        /// <summary>복귀 모드 (예: "START_TO_WAREHOUSE_TURN_AROUND")</summary>
        public string ReturnMode
        {
            get => _returnMode;
            set { _returnMode = value; OnPropertyChanged(); }
        }

        private bool _trayLoaded;
        /// <summary>트레이 적재 여부</summary>
        public bool TrayLoaded
        {
            get => _trayLoaded;
            set { _trayLoaded = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}