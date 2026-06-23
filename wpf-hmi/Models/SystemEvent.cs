using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Windows.Media;

namespace VisiPickHMI.Models
{
    public class SystemEvent
    {
        [Key]
        public int Id { get; set; }
        public string Timestamp { get; set; } = DateTime.Now.ToString("HH:mm:ss");
        public string Source { get; set; } = string.Empty;
        public string EventType { get; set; } = "INFO";
        public string Message { get; set; } = string.Empty;

        [NotMapped]
        public SolidColorBrush EventColor => EventType switch
        {
            "INFO" => new SolidColorBrush(Color.FromRgb(0x64, 0xB5, 0xF6)),
            "WARNING" => new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)),
            "ERROR" => new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
            _ => new SolidColorBrush(Color.FromRgb(0x90, 0xA4, 0xAE))
        };

        [NotMapped]
        public SolidColorBrush RowBackground => EventType switch
        {
            "WARNING" => new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xD5, 0x4F)),
            "ERROR" => new SolidColorBrush(Color.FromArgb(0x18, 0xEF, 0x53, 0x50)),
            _ => new SolidColorBrush(Colors.Transparent)
        };
    }
}
