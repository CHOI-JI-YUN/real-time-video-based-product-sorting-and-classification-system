//using System.IO;
//using System.Net.Sockets;
//using System.Text;
//using Newtonsoft.Json.Linq;

//namespace VisiPickHMI.Services
//{
//    public class TcpClientService : IDisposable
//    {
//        private TcpClient? _client;
//        private NetworkStream? _stream;
//        private CancellationTokenSource? _cts;
//        private readonly string _host;
//        private readonly int _port;

//        public event Action<string>? OnFrameReceived;       // base64 frame
//        public event Action<JObject>? OnClassificationReceived;
//        public event Action<JObject>? OnRobotStatusReceived;
//        public event Action<string>? OnConnectionStatusChanged;

//        public bool IsConnected => _client?.Connected ?? false;

//        public TcpClientService(string host = "127.0.0.1", int port = 5000)
//        {
//            _host = host;
//            _port = port;
//        }

//        /// <summary>
//        /// Python 백엔드 TCP 서버에 연결
//        /// </summary>
//        public async Task ConnectAsync()
//        {
//            try
//            {
//                _client = new TcpClient();
//                await _client.ConnectAsync(_host, _port);
//                _stream = _client.GetStream();
//                _cts = new CancellationTokenSource();
//                OnConnectionStatusChanged?.Invoke("Connected");
//                _ = Task.Run(() => ReceiveLoop(_cts.Token));
//            }
//            catch (Exception ex)
//            {
//                OnConnectionStatusChanged?.Invoke($"Error: {ex.Message}");
//            }
//        }

//        private async Task ReceiveLoop(CancellationToken token)
//        {
//            var buffer = new byte[1024 * 256]; // 256KB buffer
//            var sb = new StringBuilder();

//            try
//            {
//                while (!token.IsCancellationRequested && _stream != null)
//                {
//                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, token);
//                    if (bytesRead == 0) break;

//                    sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
//                    string data = sb.ToString();

//                    // 줄바꿈으로 메시지 구분
//                    while (data.Contains('\n'))
//                    {
//                        int idx = data.IndexOf('\n');
//                        string line = data[..idx].Trim();
//                        data = data[(idx + 1)..];

//                        if (!string.IsNullOrEmpty(line))
//                            ProcessMessage(line);
//                    }

//                    sb.Clear();
//                    sb.Append(data);
//                }
//            }
//            catch (OperationCanceledException) { }
//            catch (Exception ex)
//            {
//                OnConnectionStatusChanged?.Invoke($"Disconnected: {ex.Message}");
//            }
//        }

//        private void ProcessMessage(string json)
//        {
//            try
//            {
//                var obj = JObject.Parse(json);
//                string? msgType = obj["type"]?.ToString();

//                switch (msgType)
//                {
//                    case "frame":
//                        OnFrameReceived?.Invoke(obj["base64"]?.ToString() ?? "");
//                        break;
//                    case "classification":
//                        OnClassificationReceived?.Invoke(obj);
//                        break;
//                    case "robot_status":
//                        OnRobotStatusReceived?.Invoke(obj);
//                        break;
//                }
//            }
//            catch { /* skip malformed JSON */ }
//        }

//        /// <summary>
//        /// Python 백엔드에 JSON 명령 전송
//        /// </summary>
//        public async Task SendCommandAsync(string action, int? value = null)
//        {
//            if (_stream == null || !IsConnected) return;

//            var cmd = new JObject
//            {
//                ["type"] = "command",
//                ["action"] = action
//            };

//            if (value.HasValue)
//                cmd["value"] = value.Value;

//            string json = cmd.ToString(Newtonsoft.Json.Formatting.None) + "\n";
//            byte[] bytes = Encoding.UTF8.GetBytes(json);
//            await _stream.WriteAsync(bytes, 0, bytes.Length);
//        }

//        public void Disconnect()
//        {
//            _cts?.Cancel();
//            _stream?.Close();
//            _client?.Close();
//            OnConnectionStatusChanged?.Invoke("Disconnected");
//        }

//        public void Dispose()
//        {
//            Disconnect();
//            _cts?.Dispose();
//            _stream?.Dispose();
//            _client?.Dispose();
//        }
//    }
//}
