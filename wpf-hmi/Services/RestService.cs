using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace VisiPickHMI.Services
{
    /// <summary>
    /// Python FastAPI 제어/조회 엔드포인트 (인수인계 문서 기준)
    ///
    /// 제어 (POST):
    ///   /api/vision/start · /api/vision/stop
    ///   /api/conveyor/start · /api/conveyor/stop
    ///   /api/emergency_stop
    ///   /api/gate/{gate_no}/push        (1 또는 2)
    ///   /api/robot/transfer
    ///   /api/agv/{cmd}
    ///   /api/reset
    ///
    /// 조회 (GET):
    ///   /api/health · /api/config
    ///   /api/inspections · /api/inspections/search
    ///   /api/stats · /api/stats/spc
    ///   /api/sessions · /api/sessions/current
    ///   /api/agv/status · /api/agv/missions
    ///   /api/events
    /// </summary>
    public class RestService
    {
        private readonly HttpClient _http;

        public RestService()
        {
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
        }

        public class CommandResult
        {
            public bool Ok { get; init; }
            public bool Conflict { get; init; }
            public int StatusCode { get; init; }
            public string Message { get; init; } = "";
            public string? Body { get; init; }

            public static CommandResult FromException(string ex)
                => new() { Ok = false, StatusCode = 0, Message = ex };
        }

        /// <summary>POST — path는 /api/ 포함 전체 경로</summary>
        public async Task<CommandResult> PostAsync(string path)
        {
            string url = $"{AppConfig.ApiBaseUrl}/{path.TrimStart('/')}";
            try
            {
                using var resp = await _http.PostAsync(url, content: null);
                int code = (int)resp.StatusCode;
                string body = await resp.Content.ReadAsStringAsync();

                return new CommandResult
                {
                    Ok         = resp.IsSuccessStatusCode,
                    Conflict   = code == 409,
                    StatusCode = code,
                    Message    = resp.ReasonPhrase ?? "",
                    Body       = body
                };
            }
            catch (TaskCanceledException)
            {
                return CommandResult.FromException("응답 시간 초과 (서버 무응답)");
            }
            catch (HttpRequestException ex)
            {
                return CommandResult.FromException($"연결 실패: {ex.Message}");
            }
            catch (Exception ex)
            {
                return CommandResult.FromException(ex.Message);
            }
        }

        /// <summary>GET — 조회용</summary>
        public async Task<CommandResult> GetAsync(string path)
        {
            string url = $"{AppConfig.ApiBaseUrl}/{path.TrimStart('/')}";
            try
            {
                using var resp = await _http.GetAsync(url);
                int code = (int)resp.StatusCode;
                string body = await resp.Content.ReadAsStringAsync();

                return new CommandResult
                {
                    Ok         = resp.IsSuccessStatusCode,
                    StatusCode = code,
                    Message    = resp.ReasonPhrase ?? "",
                    Body       = body
                };
            }
            catch (TaskCanceledException)
            {
                return CommandResult.FromException("응답 시간 초과");
            }
            catch (HttpRequestException ex)
            {
                return CommandResult.FromException($"연결 실패: {ex.Message}");
            }
            catch (Exception ex)
            {
                return CommandResult.FromException(ex.Message);
            }
        }

        // ── 제어 편의 메서드 (POST) ──

        public Task<CommandResult> VisionStartAsync()      => PostAsync("api/vision/start");
        public Task<CommandResult> VisionStopAsync()       => PostAsync("api/vision/stop");
        public Task<CommandResult> ConveyorStartAsync()    => PostAsync("api/conveyor/start");
        public Task<CommandResult> ConveyorStopAsync()     => PostAsync("api/conveyor/stop");
        public Task<CommandResult> EmergencyStopAsync()    => PostAsync("api/emergency_stop");
        public Task<CommandResult> GatePushAsync(int n)    => PostAsync($"api/gate/{n}/push");
        public Task<CommandResult> RobotTransferAsync()    => PostAsync("api/robot/transfer");
        public Task<CommandResult> AgvAsync(string cmd)    => PostAsync($"api/agv/{cmd}");
        public Task<CommandResult> AgvStartAsync()         => PostAsync("api/agv/start");
        public Task<CommandResult> AgvStopAsync()          => PostAsync("api/agv/stop");
        public Task<CommandResult> ResetAsync()            => PostAsync("api/reset");

        // ── 조회 편의 메서드 (GET) ──

        public Task<CommandResult> HealthAsync()           => GetAsync("api/health");
        public Task<CommandResult> ConfigAsync()           => GetAsync("api/config");
        public Task<CommandResult> StatsAsync()            => GetAsync("api/stats");
        public Task<CommandResult> StatsSpcAsync()         => GetAsync("api/stats/spc");
        public Task<CommandResult> InspectionsAsync(int limit = 100)
            => GetAsync($"api/inspections?limit={limit}");
        public Task<CommandResult> InspectionSearchAsync(string? partType = null, string? classification = null)
        {
            var qs = new List<string>();
            if (!string.IsNullOrEmpty(partType))       qs.Add($"part_type={Uri.EscapeDataString(partType)}");
            if (!string.IsNullOrEmpty(classification)) qs.Add($"classification={Uri.EscapeDataString(classification)}");
            string query = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
            return GetAsync($"api/inspections/search{query}");
        }
        public Task<CommandResult> SessionsAsync()         => GetAsync("api/sessions");
        public Task<CommandResult> CurrentSessionAsync()   => GetAsync("api/sessions/current");
        public Task<CommandResult> AgvStatusAsync()        => GetAsync("api/agv/status");
        public Task<CommandResult> AgvMissionsAsync()      => GetAsync("api/agv/missions");
        public Task<CommandResult> EventsAsync(int limit = 100) => GetAsync($"api/events?limit={limit}");
    }
}
