using System.IO.Ports;
using System.Text;

namespace VisiPickHMI.Services
{
    public class AgvControllerService : IDisposable
    {
        private SerialPort? _serial1;
        private SerialPort? _serial2;

        public event Action<int, string>? OnAgvStatusReceived; // agvId, stateJson

        /// <summary>
        /// AGV Serial 포트 연결 (Tier 1)
        /// </summary>
        public void Connect(string comPort1 = "COM5", string comPort2 = "COM6", int baudRate = 9600)
        {
            try
            {
                _serial1 = new SerialPort(comPort1, baudRate) { ReadTimeout = 500 };
                _serial1.DataReceived += (s, e) => ReadSerial(1, _serial1);
                _serial1.Open();
            }
            catch { /* AGV1 연결 실패 — Mock 모드로 계속 */ }

            try
            {
                _serial2 = new SerialPort(comPort2, baudRate) { ReadTimeout = 500 };
                _serial2.DataReceived += (s, e) => ReadSerial(2, _serial2);
                _serial2.Open();
            }
            catch { /* AGV2 연결 실패 — Mock 모드로 계속 */ }
        }

        private void ReadSerial(int agvId, SerialPort port)
        {
            try
            {
                string data = port.ReadLine().Trim();
                OnAgvStatusReceived?.Invoke(agvId, data);
            }
            catch { }
        }

        /// <summary>
        /// AGV에 명령 전송
        /// </summary>
        public void SendCommand(int agvId, string command)
        {
            var port = agvId == 1 ? _serial1 : _serial2;
            if (port?.IsOpen == true)
            {
                port.WriteLine(command);
            }
        }

        public void GoAgv(int agvId) => SendCommand(agvId, "GO");
        public void StopAgv(int agvId) => SendCommand(agvId, "STOP");

        public void Dispose()
        {
            _serial1?.Close();
            _serial1?.Dispose();
            _serial2?.Close();
            _serial2?.Dispose();
        }
    }
}
