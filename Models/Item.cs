using Microsoft.Maui.Graphics;
using SQLite;

namespace MkalInventoryApp.Models;

public class Item
{
    [PrimaryKey]
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Other";
    public string SerialNumber { get; set; } = string.Empty;
    public string Status { get; set; } = ToolStatus.Available;
    public bool RequiresCalibration { get; set; } = true;
    public DateTime NextCalibrationDate { get; set; } = DateTime.Today.AddMonths(6);

    // Current custody (cleared on return)
    public int? CurrentBorrowerId { get; set; }
    public string CurrentBorrowerName { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.Now;

    // ---- Derived values (not stored) ----
    [Ignore] public bool CalibrationExpired => RequiresCalibration && DateTime.Today > NextCalibrationDate.Date;
    [Ignore] public int DaysToCalibration => (NextCalibrationDate.Date - DateTime.Today).Days;
    [Ignore] public bool CalibrationDueSoon => RequiresCalibration && !CalibrationExpired && DaysToCalibration <= Policy.CalibrationWarningDays;
    [Ignore] public string DisplayLabel => $"{ItemId}  {Name}";

    [Ignore]
    public Color StatusColor => Color.FromArgb(Status switch
    {
        ToolStatus.Available => "#2E7D32",
        ToolStatus.Borrowed => "#1565C0",
        ToolStatus.Overdue => "#C62828",
        ToolStatus.UnderMaintenance => "#EF6C00",
        ToolStatus.Damaged => "#AD1457",
        ToolStatus.Lost => "#424242",
        _ => "#757575"
    });

    [Ignore]
    public string CalibrationText =>
        !RequiresCalibration ? "No calibration required"
        : CalibrationExpired ? $"Calibration expired {NextCalibrationDate:dd MMM yyyy}"
        : $"Calibration due {NextCalibrationDate:dd MMM yyyy} ({DaysToCalibration} days)";

    /// <summary>Short text for the "Next Calibration Due" table column.</summary>
    [Ignore]
    public string CalibrationShort =>
        !RequiresCalibration ? "Not required"
        : CalibrationExpired ? $"EXPIRED {NextCalibrationDate:dd MMM yyyy}"
        : NextCalibrationDate.ToString("dd MMM yyyy");

    [Ignore]
    public Color CalibrationColor => Color.FromArgb(!RequiresCalibration ? "#6B7280"
        : CalibrationExpired ? "#C62828"
        : CalibrationDueSoon ? "#EF6C00"
        : "#374151");

    [Ignore]
    public string CustodyText => CurrentBorrowerId.HasValue
        ? $"With {CurrentBorrowerName}, due {DueDate:dd MMM HH:mm}"
        : "In the shop";

    /// <summary>Short text for the "Custody / Borrower" table column.</summary>
    [Ignore] public string CustodyShort => CurrentBorrowerId.HasValue ? CurrentBorrowerName : "In the shop";

    [Ignore]
    public string SubtitleText => string.IsNullOrWhiteSpace(SerialNumber)
        ? Category
        : $"{Category}, S/N {SerialNumber}";
}
