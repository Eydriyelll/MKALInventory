using System.Text.RegularExpressions;
using SQLite;
using MkalInventoryApp.Models;

namespace MkalInventoryApp.Services;

public record DashboardStats(
    int Total, int Available, int Borrowed, int Overdue,
    int Quarantined, int Lost, int CalibrationAlerts, int ActiveReservations);

/// <summary>
/// Module: Equipment calibration, safety lockout and inventory.
/// Asset catalog, calibration scheduling, and the lockout that keeps uncalibrated or compromised tools off the job.
/// </summary>
public class InventoryService : ServiceBase
{
    public InventoryService(AppDatabase database) : base(database) { }

    // ------------------------------------------------------------------
    // Reading
    // ------------------------------------------------------------------
    public async Task<DashboardStats> GetStatsAsync()
    {
        var db = await Database.GetAsync();
        var items = await db.Table<Item>().ToListAsync();
        var reservations = await db.Table<Reservation>().Where(r => r.Status == ReservationStatus.Active).CountAsync();
        return new DashboardStats(
            items.Count,
            items.Count(i => i.Status == ToolStatus.Available),
            items.Count(i => i.Status == ToolStatus.Borrowed),
            items.Count(i => i.Status == ToolStatus.Overdue),
            items.Count(i => i.Status is ToolStatus.UnderMaintenance or ToolStatus.Damaged),
            items.Count(i => i.Status == ToolStatus.Lost),
            items.Count(i => i.CalibrationExpired || i.CalibrationDueSoon),
            reservations);
    }

    /// <summary>Tools an administrator should look at first: overdue, lost, quarantined, or calibration expired/due soon.</summary>
    public async Task<List<Item>> GetAttentionItemsAsync()
    {
        var db = await Database.GetAsync();
        var items = await db.Table<Item>().ToListAsync();

        static int Rank(Item i) => i.Status switch
        {
            ToolStatus.Overdue => 0,
            ToolStatus.Lost => 1,
            ToolStatus.Damaged => 2,
            ToolStatus.UnderMaintenance => 3,
            _ => i.CalibrationExpired ? 4 : 5
        };

        return items
            .Where(i => i.Status is ToolStatus.Overdue or ToolStatus.Lost or ToolStatus.Damaged or ToolStatus.UnderMaintenance
                        || i.CalibrationExpired || i.CalibrationDueSoon)
            .OrderBy(Rank).ThenBy(i => i.NextCalibrationDate)
            .ToList();
    }

    public async Task<List<Item>> GetItemsAsync(string? search = null, string? category = null,
        string? status = null, string sort = "Name")
    {
        var db = await Database.GetAsync();
        IEnumerable<Item> items = await db.Table<Item>().ToListAsync();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            items = items.Where(i =>
                i.ItemId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.SerialNumber.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.CurrentBorrowerName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrEmpty(category)) items = items.Where(i => i.Category == category);
        if (!string.IsNullOrEmpty(status)) items = items.Where(i => i.Status == status);

        items = sort switch
        {
            "Tool ID" => items.OrderBy(i => i.ItemId),
            "Status" => items.OrderBy(i => i.Status).ThenBy(i => i.Name),
            "Calibration due" => items.OrderBy(i => i.RequiresCalibration ? 0 : 1).ThenBy(i => i.NextCalibrationDate),
            _ => items.OrderBy(i => i.Name)
        };
        return items.ToList();
    }

    public async Task<Item?> GetItemAsync(string itemId)
    {
        var db = await Database.GetAsync();
        return await db.FindAsync<Item>(NormId(itemId));
    }

    public async Task<List<Item>> GetHeldItemsAsync(int userId)
    {
        var db = await Database.GetAsync();
        return (await db.Table<Item>().ToListAsync())
            .Where(i => i.CurrentBorrowerId == userId).OrderBy(i => i.ItemId).ToList();
    }

    // ------------------------------------------------------------------
    // Calibration lockout (Policy 1). Called by the policy scan.
    // ------------------------------------------------------------------
    internal static int EnforceCalibration(SQLiteConnection c, User? actor)
    {
        var locked = 0;
        foreach (var item in c.Table<Item>().ToList())
        {
            if (item.Status == ToolStatus.Available && item.CalibrationExpired)
            {
                item.Status = ToolStatus.UnderMaintenance;
                c.Update(item);
                Audit(c, item.ItemId, actor, "Calibration lockout",
                    $"Calibration expired {item.NextCalibrationDate:dd MMM yyyy}. Tool locked as Under Maintenance.");
                locked++;
            }
        }
        return locked;
    }

    // ------------------------------------------------------------------
    // Catalog management (admin only)
    // ------------------------------------------------------------------
    private static string? ValidateItem(Item i)
    {
        var id = NormId(i.ItemId);
        if (!Regex.IsMatch(id, @"^[A-Z0-9][A-Z0-9\-]{1,19}$"))
            return "Tool ID must be 2 to 20 characters: letters, numbers and hyphens (for example ISO-001).";
        if (string.IsNullOrWhiteSpace(i.Name)) return "Tool name is required.";
        if (i.Name.Trim().Length > 60) return "Tool name must be 60 characters or fewer.";
        return null;
    }

    public async Task<OpResult> AddItemAsync(User actor, Item item)
    {
        var error = ValidateItem(item);
        if (error != null) return OpResult.Fail(error);

        item.ItemId = NormId(item.ItemId);
        item.Name = item.Name.Trim();
        item.Description = item.Description?.Trim() ?? string.Empty;
        item.SerialNumber = item.SerialNumber?.Trim() ?? string.Empty;
        item.DateAdded = DateTime.Now;
        item.CurrentBorrowerId = null;
        item.CurrentBorrowerName = string.Empty;
        item.DueDate = null;
        item.Status = item.CalibrationExpired ? ToolStatus.UnderMaintenance : ToolStatus.Available;

        var db = await Database.GetAsync();
        var result = OpResult.Fail("Could not add the tool.");
        await db.RunInTransactionAsync(c =>
        {
            if (!IsAdmin(c, actor)) { result = OpResult.AdminOnly; return; }
            if (c.Find<Item>(item.ItemId) != null)
            {
                result = OpResult.Fail($"A tool with ID {item.ItemId} already exists.");
                return;
            }
            c.Insert(item);
            Audit(c, item.ItemId, actor, "Tool registered", $"{item.Name} ({item.Category}) added. Status: {item.Status}.");
            result = OpResult.Success(item.Status == ToolStatus.UnderMaintenance
                ? $"{item.Name} registered, but its calibration date has passed so it is locked as Under Maintenance."
                : $"{item.Name} registered and available.");
        });
        return result;
    }

    public async Task<OpResult> UpdateItemAsync(User actor, Item edited)
    {
        var error = ValidateItem(edited);
        if (error != null) return OpResult.Fail(error);

        var db = await Database.GetAsync();
        var result = OpResult.Fail("Could not save changes.");
        await db.RunInTransactionAsync(c =>
        {
            if (!IsAdmin(c, actor)) { result = OpResult.AdminOnly; return; }
            var item = c.Find<Item>(NormId(edited.ItemId));
            if (item == null) { result = OpResult.Fail("Tool not found."); return; }

            item.Name = edited.Name.Trim();
            item.Description = edited.Description?.Trim() ?? string.Empty;
            item.Category = edited.Category;
            item.SerialNumber = edited.SerialNumber?.Trim() ?? string.Empty;
            item.RequiresCalibration = edited.RequiresCalibration;
            item.NextCalibrationDate = edited.NextCalibrationDate;

            var note = "Details updated.";
            if (item.Status == ToolStatus.Available && item.CalibrationExpired)
            {
                item.Status = ToolStatus.UnderMaintenance;
                note += " The calibration date has passed, so the tool was locked as Under Maintenance.";
            }
            c.Update(item);
            Audit(c, item.ItemId, actor, "Tool updated",
                $"{item.Name}. Calibration: {(item.RequiresCalibration ? item.NextCalibrationDate.ToString("dd MMM yyyy") : "not required")}.");
            result = OpResult.Success(note);
        });
        return result;
    }

    public async Task<OpResult> DeleteItemAsync(User actor, string itemId)
    {
        itemId = NormId(itemId);
        var db = await Database.GetAsync();
        var result = OpResult.Fail("Could not delete the tool.");
        await db.RunInTransactionAsync(c =>
        {
            if (!IsAdmin(c, actor)) { result = OpResult.AdminOnly; return; }
            var item = c.Find<Item>(itemId);
            if (item == null) { result = OpResult.Fail("Tool not found."); return; }
            if (item.Status is ToolStatus.Borrowed or ToolStatus.Overdue)
            {
                result = OpResult.Fail($"{item.Name} is checked out to {item.CurrentBorrowerName}. It must be returned before it can be deleted.");
                return;
            }

            foreach (var r in c.Table<Reservation>().Where(r => r.ItemId == itemId && r.Status == ReservationStatus.Active).ToList())
            {
                r.Status = ReservationStatus.Cancelled;
                c.Update(r);
            }
            c.Delete<Item>(itemId);
            Audit(c, itemId, actor, "Tool deleted", $"{item.Name} removed from the catalog. Past records are kept.");
            result = OpResult.Success($"{item.Name} deleted. Its history stays in the audit ledger.");
        });
        return result;
    }

    public async Task<OpResult> LockForCalibrationAsync(User actor, string itemId)
    {
        itemId = NormId(itemId);
        var db = await Database.GetAsync();
        var result = OpResult.Fail("Could not lock the tool.");
        await db.RunInTransactionAsync(c =>
        {
            if (!IsAdmin(c, actor)) { result = OpResult.AdminOnly; return; }
            var item = c.Find<Item>(itemId);
            if (item == null) { result = OpResult.Fail("Tool not found."); return; }
            if (item.Status != ToolStatus.Available)
            {
                result = OpResult.Fail($"Only available tools can be locked. {item.Name} is currently {item.Status}.");
                return;
            }
            item.Status = ToolStatus.UnderMaintenance;
            c.Update(item);
            Audit(c, itemId, actor, "Locked for calibration", $"{item.Name} set to Under Maintenance by an administrator.");
            result = OpResult.Success($"{item.Name} is locked for calibration and cannot be checked out.");
        });
        return result;
    }

    /// <summary>Policy 2: only an administrator can clear a quarantined tool back to Available.</summary>
    public async Task<OpResult> ReleaseItemAsync(User actor, string itemId)
    {
        itemId = NormId(itemId);
        var db = await Database.GetAsync();
        var result = OpResult.Fail("Could not release the tool.");
        await db.RunInTransactionAsync(c =>
        {
            if (!IsAdmin(c, actor)) { result = OpResult.AdminOnly; return; }
            var item = c.Find<Item>(itemId);
            if (item == null) { result = OpResult.Fail("Tool not found."); return; }
            if (item.Status is not (ToolStatus.UnderMaintenance or ToolStatus.Damaged or ToolStatus.Lost))
            {
                result = OpResult.Fail($"{item.Name} is {item.Status}. Only tools that are Under Maintenance, Damaged or Lost can be released.");
                return;
            }
            if (item.CalibrationExpired)
            {
                result = OpResult.Fail("Calibration has expired. Edit the tool and enter the new calibration date first.");
                return;
            }
            var previous = item.Status;
            item.Status = ToolStatus.Available;
            c.Update(item);
            Audit(c, itemId, actor, "Released to available", $"Cleared from {previous} by an administrator.");
            result = OpResult.Success($"{item.Name} is available again.");
        });
        return result;
    }
}
