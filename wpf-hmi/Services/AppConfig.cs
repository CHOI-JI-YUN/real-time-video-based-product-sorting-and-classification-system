using System.IO;
using Newtonsoft.Json.Linq;

namespace VisiPickHMI.Services
{
    /// <summary>
    /// appsettings.json에서 서버 접속 정보를 읽어온다.
    /// 하드코딩 대신 외부 설정 파일을 사용하므로, 시연 당일 서버 IP가
    /// 바뀌어도 재빌드 없이 appsettings.json만 고치면 된다.
    /// </summary>
    public static class AppConfig
    {
        // 파일이 없거나 키가 비었을 때 사용할 기본값
        public static string Host { get; private set; } = "127.0.0.1";
        public static int MqttPort { get; private set; } = 1883;
        public static int ApiPort { get; private set; } = 8000;

        /// <summary>로그인 화면 "현장 CCTV"에서 사용할 로컬 USB 웹캠 장치 번호 (보통 0).</summary>
        public static int SiteCameraIndex { get; private set; } = 0;

        static AppConfig()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (!File.Exists(path)) return;

                var root = JObject.Parse(File.ReadAllText(path));
                var server = root["Server"];
                if (server == null) return;

                Host     = server["Host"]?.ToString() ?? Host;
                MqttPort = server["MqttPort"]?.ToObject<int?>() ?? MqttPort;
                ApiPort  = server["ApiPort"]?.ToObject<int?>()  ?? ApiPort;

                var cam = root["SiteCamera"];
                if (cam != null)
                    SiteCameraIndex = cam["DeviceIndex"]?.ToObject<int?>() ?? SiteCameraIndex;
            }
            catch
            {
                // 설정 파일이 깨졌어도 기본값으로 계속 동작
            }
        }

        /// <summary>제어용 REST API 베이스 URL — 예: http://127.0.0.1:8000</summary>
        public static string ApiBaseUrl => $"http://{Host}:{ApiPort}";
    }
}
