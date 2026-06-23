using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using VisiPickHMI.Models;

namespace VisiPickHMI.Services
{
    public class CsvExportService
    {
        /// <summary>
        /// 이벤트 로그를 CSV 파일로 내보내기
        /// </summary>
        public static string ExportLogs(ObservableCollection<SystemEvent> events, string? filePath = null)
        {
            filePath ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"VisiPick_Log_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("날짜,시간,장비,유형,메시지");

            foreach (var e in events)
            {
                string msg = e.Message.Replace("\"", "\"\"");
                string[] parts = e.Timestamp.Split(' ');
                string date = parts.Length > 0 ? parts[0] : e.Timestamp;
                string time = parts.Length > 1 ? parts[1] : "";
                sb.AppendLine($"=\"{date}\",\"{time}\",\"{e.Source}\",\"{e.EventType}\",\"{msg}\"");
            }

            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
            return filePath;
        }

        /// <summary>
        /// 검사 결과를 CSV 파일로 내보내기
        /// </summary>
        public static string ExportInspectionResults(IEnumerable<InspectionResult> results, string? filePath = null)
        {
            filePath ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"VisiPick_Inspection_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Id,날짜,시간,부품유형,클래스,결함코드,결과,신뢰도,사이클타임(ms),게이트");

            foreach (var r in results)
            {
                string[] parts = (r.Timestamp ?? "").Split(' ');
                string date = parts.Length > 0 ? parts[0] : r.Timestamp ?? "";
                string time = parts.Length > 1 ? parts[1] : "";
                sb.AppendLine($"{r.Id},=\"{date}\",\"{time}\",\"{r.ComponentType}\",\"{r.Class}\"," +
                              $"\"{r.DefectCode}\",\"{r.Result}\",{r.Confidence},{r.CycleTimeMs},{r.GateUsed}");
            }

            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
            return filePath;
        }
    }
}