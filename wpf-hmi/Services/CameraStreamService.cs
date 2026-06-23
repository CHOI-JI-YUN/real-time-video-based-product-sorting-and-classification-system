using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;

namespace VisiPickHMI.Services
{
    /// <summary>
    /// Python FastAPI 영상 엔드포인트 (인수인계 문서 기준):
    ///   GET /video/top   · /video/side    — MJPEG 스트림
    ///   GET /snapshot/top · /snapshot/side — 최신 1프레임 JPEG
    ///
    /// 두 카메라를 동시에 폴링한다.
    ///   cameraId=1 → /snapshot/top   (상부 카메라)
    ///   cameraId=2 → /snapshot/side  (측면 카메라)
    /// </summary>
    public class CameraStreamService
    {
        private readonly HttpClient _http;
        private readonly Dictionary<int, CancellationTokenSource> _ctsSources = new();

        /// <summary>새 프레임 도착 시 호출 — (cameraId, frame)</summary>
        public event Action<int, BitmapSource>? OnFrame;
        public event Action<int, string>? OnError;

        public bool IsRunning(int cameraId) =>
            _ctsSources.TryGetValue(cameraId, out var cts) && !cts.IsCancellationRequested;

        public CameraStreamService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        }

        /// <summary>
        /// 카메라 폴링 시작.
        /// cameraId: 1=top, 2=side
        /// </summary>
        public void Start(int cameraId = 1, int fps = 10)
        {
            Stop(cameraId);
            var cts = new CancellationTokenSource();
            _ctsSources[cameraId] = cts;

            int delayMs = Math.Max(33, 1000 / Math.Max(1, fps));
            _ = PollLoopAsync(cameraId, delayMs, cts.Token);
        }

        /// <summary>
        /// 두 카메라 동시 시작 편의 메서드
        /// </summary>
        public void StartBoth(int fps = 10)
        {
            Start(cameraId: 1, fps: fps);
            Start(cameraId: 2, fps: fps);
        }

        public void Stop(int cameraId)
        {
            if (_ctsSources.TryGetValue(cameraId, out var cts))
            {
                cts.Cancel();
                _ctsSources.Remove(cameraId);
            }
        }

        public void StopAll()
        {
            foreach (var kvp in _ctsSources.ToList())
            {
                kvp.Value.Cancel();
            }
            _ctsSources.Clear();
        }

        /// <summary>cameraId → 스냅샷 URL 매핑</summary>
        private static string GetSnapshotUrl(int cameraId)
        {
            string name = cameraId switch
            {
                2 => "side",
                _ => "top"
            };
            return $"{AppConfig.ApiBaseUrl}/snapshot/{name}";
        }

        /// <summary>cameraId → MJPEG 스트림 URL (참고용)</summary>
        public static string GetStreamUrl(int cameraId)
        {
            string name = cameraId switch
            {
                2 => "side",
                _ => "top"
            };
            return $"{AppConfig.ApiBaseUrl}/video/{name}";
        }

        private async Task PollLoopAsync(int cameraId, int delayMs, CancellationToken token)
        {
            string url = GetSnapshotUrl(cameraId);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    byte[] bytes = await _http.GetByteArrayAsync(url, token);

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            using var ms = new MemoryStream(bytes);
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = ms;
                            bmp.EndInit();
                            bmp.Freeze();
                            OnFrame?.Invoke(cameraId, bmp);
                        }
                        catch { /* 손상된 프레임 스킵 */ }
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(cameraId, ex.Message);
                    try { await Task.Delay(500, token); } catch { break; }
                }

                try { await Task.Delay(delayMs, token); }
                catch { break; }
            }
        }
    }
}
