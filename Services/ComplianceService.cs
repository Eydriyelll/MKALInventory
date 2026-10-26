using MkalInventoryApp.Models;

namespace MkalInventoryApp.Services;

public record ScanResult(int NewlyOverdue, int NewlyLocked, int ExpiredReservations);
public record LedgerCheck(bool Intact, int Entries, int? FirstBrokenId);

/// <summary>
/// Module: Audit ledger, accountability scoring and compliance export.
/// Tamper-evident audit trail, delinquency risk scoring, and one-click CSV reports.
/// </summary>
public class ComplianceService : ServiceBase
{
    public ComplianceService(AppDatabase database) : base(database) { }

    // ------------------------------------------------------------------
    // Policy scan: calibration lockout, overdue + risk scoring, reservation expiry
    // ------------------------------------------------------------------
    public async Task<ScanResult> RunPolicyScanAsync(User? actor = null)
    {
        var db = await Database.GetAsync();
        int overdue = 0, locked = 0, expired = 0;

        await db.RunInTransactionAsync(c =>
        {
            var now = DateTime.Now;

            // Policy 1
            locked = InventoryService.EnforceCalibration(c, actor);

            // Policy 4: unreturned tools past their due time become Overdue (once per loan) and raise the risk score.
            var openLoans = c.Table<LoanRecord>().Where(l => l.ReturnDate == null).ToList();
            foreach (var loan in openLoans.Where(l => !l.OverdueFlagged && l.DueDate < now))
            {
                var item = c.Find<Item>(loan.ItemId);
                if (item != null && item.Status == ToolStatus.Borrowed)
                {
                    item.Status = ToolStatus.Overdue;
                    c.Update(item);
                }

                loan.OverdueFlagged = true;
                c.Update(loan);

                var borrower = c.Find<User>(loan.UserId);
                if (borrower != null)
                {
                    borrower.RiskScore += Policy.OverdueRiskPoints;
                    c.Update(borrower);
                }

                Audit(c, loan.ItemId, actor, "Marked overdue",
                    $"{loan.BorrowerName} did not return by {loan.DueDate:dd MMM HH:mm}. Risk score +{Policy.OverdueRiskPoints}.");
                overdue++;
            }

            // Reservations whose date has passed without checkout expire.
            foreach (var r in c.Table<Reservation>().Where(r => r.Status == ReservationStatus.Active).ToList()
                         .Where(r => r.ReserveDate.Date < now.Date))
            {
                r.Status = ReservationStatus.Expired;
                c.Update(r);
                expired++;
            }
        });

        return new ScanResult(overdue, locked, expired);
    }

    // ------------------------------------------------------------------
    // Audit ledger
    // ------------------------------------------------------------------
    public async Task<List<AuditLog>> GetAuditAsync(string? itemId = null, string? search = null, int limit = 200)
    {
        var db = await Database.GetAsync();
        IEnumerable<AuditLog> rows = await db.Table<AuditLog>().OrderByDescending(a => a.Id).ToListAsync();
        if (!string.IsNullOrWhiteSpace(itemId)) rows = rows.Where(a => a.ItemId == NormId(itemId));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            rows = rows.Where(a =>
                a.Action.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.ItemId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.UserName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Details.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        return rows.Take(limit).ToList();
    }

    /// <summary>Recomputes every hash and link. Any edited or deleted entry shows up as the first broken id.</summary>
    public async Task<LedgerCheck> VerifyLedgerAsync()
    {
        var db = await Database.GetAsync();
        var rows = await db.Table<AuditLog>().OrderBy(a => a.Id).ToListAsync();
        var expectedPrev = "GENESIS";
        foreach (var e in rows)
        {
            if (e.PrevHash != expectedPrev || e.Hash != AppDatabase.ComputeHash(e))
                return new LedgerCheck(false, rows.Count, e.Id);
            expectedPrev = e.Hash;
        }
        return new LedgerCheck(true, rows.Count, null);
    }

    // ------------------------------------------------------------------
    // Reports (admin only)
    // ------------------------------------------------------------------
    public async Task<(OpResult Result, string FileName, string Csv)> BuildReportAsync(User actor, CsvReport report)
    {
        if (!await IsAdminAsync(actor)) return (OpResult.AdminOnly, string.Empty, string.Empty);
        var csv = await report.BuildAsync(Database);
        var db = await Database.GetAsync();
        await db.RunInTransactionAsync(c => Audit(c, string.Empty, actor, "Report exported", report.Title));
        return (OpResult.Success(), report.FileName(DateTime.Now), csv);
    }
}
