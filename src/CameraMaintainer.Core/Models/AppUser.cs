namespace CameraMaintainer.Core.Models;

public sealed class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string Role { get; set; } = "Viewer";
    public bool CanViewDashboard { get; set; } = true;
    public bool CanViewCameras { get; set; } = true;
    public bool CanViewAlerts { get; set; } = true;
    public bool CanViewAlertHistory { get; set; } = true;
    public bool CanViewMaintenance { get; set; } = true;
    public bool CanRunChecks { get; set; }
    public bool CanManageCameras { get; set; }
    public bool CanManageUsers { get; set; }
    public string? AlertBoardColumnKeys { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
