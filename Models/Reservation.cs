using SQLite;

namespace MkalInventoryApp.Models;

public class Reservation
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime ReserveDate { get; set; }
    public string Project { get; set; } = string.Empty;
    public string Status { get; set; } = ReservationStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Ignore] public bool CanCancel { get; set; }
    [Ignore] public string TitleText => $"{ItemId}  {ItemName}";
    [Ignore] public string DateText => ReserveDate.ToString("ddd, dd MMM yyyy");
    [Ignore] public string OwnerText => string.IsNullOrWhiteSpace(Project) ? UserName : $"{UserName}, {Project}";
}
