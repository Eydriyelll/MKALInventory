using Microsoft.Maui.Graphics;
using SQLite;

namespace MkalInventoryApp.Models;

public class User
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    [Unique]
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = UserRole.User;
    public string JobTitle { get; set; } = "Journeyman";

    /// <summary>Registered sign-off initials. They must be entered with the PIN at every handover.</summary>
    public string Initials { get; set; } = string.Empty;

    // Salted PBKDF2 hashes. Plain passwords and PINs are never stored.
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty;
    public string PinSalt { get; set; } = string.Empty;

    public int RiskScore { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Ignore] public bool IsAdmin => Role == UserRole.Admin;
    [Ignore] public string RoleText => IsAdmin ? "Administrator / QTP" : "Field technician";
    [Ignore] public string TitleText => $"{RoleText}, {JobTitle}";
    [Ignore] public string StatusText => IsActive ? "Active" : "Deactivated";
    [Ignore] public string ActiveActionText => IsActive ? "Deactivate" : "Activate";
    [Ignore] public string RiskText => $"Risk score {RiskScore} ({Policy.RiskBand(RiskScore)})";
    [Ignore] public Color RiskColor => Color.FromArgb(RiskScore < 20 ? "#2E7D32" : RiskScore < 50 ? "#EF6C00" : "#C62828");
}
