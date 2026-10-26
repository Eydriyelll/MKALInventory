using SQLite;

namespace MkalInventoryApp.Models;

/// <summary>
/// Append-only, hash-chained ledger. Each entry stores the hash of the one before it,
/// so any edit or deletion later breaks the chain and is detected by "Verify ledger".
/// </summary>
public class AuditLog
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string ItemId { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string PrevHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;

    [Ignore] public string TimeText => Timestamp.ToString("dd MMM yyyy HH:mm");
    [Ignore] public string Headline => string.IsNullOrEmpty(ItemId) ? Action : $"{Action}: {ItemId}";
    [Ignore] public string ByText => $"by {UserName}";
}
