using System.ComponentModel.DataAnnotations;

namespace VisiPickHMI.Models
{
    public class SystemSettings
    {
        [Key]
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;   // Conveyor / Vision / Quality / System
        public string UpdatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        public string UpdatedBy { get; set; } = string.Empty;
    }
}
