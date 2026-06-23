using System.ComponentModel.DataAnnotations;

namespace VisiPickHMI.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;  // SHA256 해시

        public string DisplayName { get; set; } = string.Empty;

        // "Admin" | "Operator" | "Viewer"
        public string Role { get; set; } = "Operator";

        public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        public bool IsActive { get; set; } = true;
    }
}
