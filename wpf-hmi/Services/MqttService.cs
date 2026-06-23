using System.Text;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using Newtonsoft.Json.Linq;

namespace VisiPickHMI.Services
{
    /// <summary>
    /// MQTT 토픽 (Python FastAPI 백엔드 인수인계 기준):
    ///   visipick/inspection          — 검사 결과 1건
    ///   visipick/system/state        — FSM 상태 전이 (IDLE/RUNNING/…)
    ///   visipick/system/event        — 이벤트 로그
    ///   visipick/agv/{id}/status     — AGV 상태
    ///
    /// 제어는 REST(POST)로만 수행하므로 Publish 메서드는 제거됨.
    /// 영상은 MJPEG HTTP로 별도 수신(CameraStreamService).
    /// </summary>
    public class MqttService
    {
        private IMqttClient? _client;

        // ── 수신 이벤트 ──
        public event Action<JObject>? OnInspectionReceived;      // visipick/inspection
        public event Action<JObject>? OnSystemStateReceived;     // visipick/system/state
        public event Action<JObject>? OnSystemEventReceived;     // visipick/system/event
        public event Action<JObject>? OnAgvStatusReceived;       // visipick/agv/+/status
        public event Action<JObject>? OnAgvRfidReceived;         // visipick/agv/+/rfid

        public bool IsConnected => _client?.IsConnected == true;

        public async Task ConnectAsync(string brokerIp = "127.0.0.1", int port = 1883)
        {
            var factory = new MqttFactory();
            _client = factory.CreateMqttClient();

            _client.DisconnectedAsync += e => Task.CompletedTask;

            _client.ApplicationMessageReceivedAsync += e =>
            {
                string topic = e.ApplicationMessage.Topic;
                string payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

                try
                {
                    var obj = JObject.Parse(payload);

                    if (topic == "visipick/inspection")
                        OnInspectionReceived?.Invoke(obj);

                    else if (topic == "visipick/system/state")
                        OnSystemStateReceived?.Invoke(obj);

                    else if (topic == "visipick/system/event")
                        OnSystemEventReceived?.Invoke(obj);

                    else if (topic.StartsWith("visipick/agv/") && topic.EndsWith("/status"))
                        OnAgvStatusReceived?.Invoke(obj);

                    else if (topic.StartsWith("visipick/agv/") && topic.EndsWith("/rfid"))
                        OnAgvRfidReceived?.Invoke(obj);
                }
                catch
                {
                    // 잘못된 JSON 무시
                }

                return Task.CompletedTask;
            };

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(brokerIp, port)
                .WithClientId("VisiPick_WPF_HMI")
                .Build();

            await _client.ConnectAsync(options);

            // visipick/# 와일드카드로 한 번에 구독
            var subscribeOptions = factory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter(f => f
                    .WithTopic("visipick/#")
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                .Build();

            await _client.SubscribeAsync(subscribeOptions);
        }

        public async Task DisconnectAsync()
        {
            if (_client?.IsConnected == true)
                await _client.DisconnectAsync();
        }

        /// <summary>
        /// 지정 토픽에 문자열 payload 발행 (AGV 명령 전송용)
        /// </summary>
        public async Task PublishAsync(string topic, string payload)
        {
            if (_client?.IsConnected != true) return;

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(payload))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _client.PublishAsync(message);
        }
    }
}