using System.Text;
using SQLite;
using MkalInventoryApp.Models;

namespace MkalInventoryApp.Services;

/// <summary>
/// Template for every CSV export. Subclasses supply a title, headers and rows (polymorphism);
/// the base class turns them into a CSV file.
/// </summary>
public abstract class CsvReport
{
    public abstract string Title { get; }
    protected abstract string FilePrefix { get; }
    protected abstract string[] Headers { get; }
    protected abstract Task<List<object?[]>> LoadRowsAsync(SQLiteAsyncConnection db);

    public string FileName(DateTime now) => $"{FilePrefix}_{now:yyyyMMdd_HHmm}.csv";

    public async Task<string> BuildAsync(AppDatabase database)
    {
        var db = await database.GetAsync();
        var rows = await LoadRowsAsync(db);
        var sb = new StringBuilder();
        sb.AppendLine(CsvHelper.Row(Headers.Cast<object?>().ToArray()));
        foreach (var row in rows) sb.AppendLine(CsvHelper.Row(row));
        return sb.ToString();
    }
}

/// <summary>Closeout report: equipment, technician, checkout duration, return condition, incident flags.</summary>
public sealed class CustodyReport : CsvReport
{
    public override string Title => "Custody audit report";
    protected override string FilePrefix => "mkal_custody_report";
    protected override string[] Headers => new[]
    {
        "Loan ID", "Tool ID", "Tool Name", "Technician", "Checked Out", "Due", "Returned", "Duration (hours)",
        "Loan Status", "Return Condition", "Flagged Overdue", "Handover Initials", "Return Notes", "Inspection Photo"
    };

    protected override async Task<List<object?[]>> LoadRowsAsync(SQLiteAsyncConnection db)
    {
        var loans = await db.Table<LoanRecord>().OrderBy(l => l.BorrowDate).ToListAsync();
        return loans.Select(l =>
        {
            var end = l.ReturnDate ?? DateTime.Now;
            var status = l.ReturnDate != null ? "Returned" : l.DueDate < DateTime.Now ? "Open, overdue" : "Open";
            return new object?[]
            {
                l.Id, l.ItemId, l.ItemName, l.BorrowerName, l.BorrowDate, l.DueDate, l.ReturnDate,
                Math.Round((end - l.BorrowDate).TotalHours, 1), status, l.ReturnCondition, l.OverdueFlagged,
                l.SignatureInitials, l.ReturnNotes, l.PhotoPath
            };
        }).ToList();
    }
}

public sealed class AuditLedgerReport : CsvReport
{
    public override string Title => "Full audit ledger";
    protected override string FilePrefix => "mkal_audit_ledger";
    protected override string[] Headers => new[] { "Entry ID", "Timestamp", "Tool ID", "User", "Action", "Details", "Hash" };

    protected override async Task<List<object?[]>> LoadRowsAsync(SQLiteAsyncConnection db)
    {
        var rows = await db.Table<AuditLog>().OrderBy(a => a.Id).ToListAsync();
        return rows.Select(a => new object?[] { a.Id, a.Timestamp, a.ItemId, a.UserName, a.Action, a.Details, a.Hash }).ToList();
    }
}

public sealed class InventoryReport : CsvReport
{
    public override string Title => "Inventory snapshot";
    protected override string FilePrefix => "mkal_inventory";
    protected override string[] Headers => new[]
    {
        "Tool ID", "Name", "Description", "Category", "Serial Number", "Status",
        "Calibration Required", "Next Calibration", "Current Borrower", "Due"
    };

    protected override async Task<List<object?[]>> LoadRowsAsync(SQLiteAsyncConnection db)
    {
        var items = (await db.Table<Item>().ToListAsync()).OrderBy(i => i.ItemId);
        return items.Select(i => new object?[]
        {
            i.ItemId, i.Name, i.Description, i.Category, i.SerialNumber, i.Status,
            i.RequiresCalibration, i.RequiresCalibration ? i.NextCalibrationDate : null, i.CurrentBorrowerName, i.DueDate
        }).ToList();
    }
}
