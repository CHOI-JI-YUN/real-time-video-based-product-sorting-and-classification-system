using System.ComponentModel.DataAnnotations;

namespace VisiPickHMI.Models
{
    public class AgvMission
    {
        [Key]
        public int Id { get; set; }
        public int AgvId { get; set; }
        public string StartTime { get; set; } = string.Empty;
        public string? EndTime { get; set; }
        public string Source { get; set; } = "적재 포인트";
        public string Destination { get; set; } = "공정 노드";
        public string TrayClass { get; set; } = string.Empty;   // A / B / C
        public int ItemCount { get; set; }
        public string Status { get; set; } = "대기";             // 대기 / 운반중 / 완료 / 오류
    }
}