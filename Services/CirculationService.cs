using SQLite;
using MkalInventoryApp.Models;

namespace MkalInventoryApp.Services;

/// <summary>
/// Module: Field circulation, project staging and return assessment.
/// Checkout, condition-graded returns, and job-site date reservations.
/// </summary>
public class CirculationService : ServiceBase
{
    public CirculationService(AppDatabase database) : base(database) { }

    // ------------------------------------------------------------------
    // Checkout (Policies 1, 3, reservations)
    // ------------------------------------------------------------------
    public async Task<OpResult> BorrowAsync(User user, string itemId, string pin, string initials,
        string signatureData, string returnWindow)
    {
        itemId = NormId(itemId);
        initials = (initials ?? string.Empty).Trim().ToUpperInvariant();

        if (itemId.Length == 0) return OpResult.Fail("Enter the tool ID.");
        if (!SecurityHelper.IsValidPin(pin)) return OpResult.Fail("Enter your 4-digit PIN.");
        if (!SecurityHelper.IsValidInitials(initials)) return OpResult.Fail("Enter your sign-off initials (2 to 4 letters).");

        var db = await Database.GetAsync();
        var result = OpResult.Fail("Checkout could not be completed.");
        await db.RunInTransactionAsync(c =>
        {
            var me = c.Find<User>(user.Id);
            if (me == null || !me.IsActive) { result = OpResult.Fail("Your account is not active."); return; }

            // Policy 3: PIN and initials sign-off before any tool leaves the shop.
            var verifyError = AuthService.VerifyHandover(me, pin, initials);
            if (verifyError != null)
            {
                Audit(c, itemId, me, "Handover denied", verifyError.Contains("PIN") ? "Incorrect PIN." : "Initials did not match.");
                result = OpResult.Fail(verifyError);
                return;
            }

            var item = c.Find<Item>(itemId);
            if (item == null) { result = OpResult.Fail("Tool ID not found."); return; }
            if (item.Status != ToolStatus.Available)
            {
                Audit(c, itemId, me, "Handover denied", $"Tool status is {item.Status}.");
                result = OpResult.Fail(item.Status switch
                {
                    ToolStatus.Borrowed or ToolStatus.Overdue => $"Already checked out to {item.CurrentBorrowerName}.",
                    ToolStatus.UnderMaintenance => "Under Maintenance. It cannot be issued until an administrator clears it.",
                    ToolStatus.Damaged => "Quarantined as Damaged. It needs administrator clearance.",
                    ToolStatus.Lost => "Marked as Lost. Report to the administrator.",
                    _ => $"Tool cannot be issued. Current status: {item.Status}."
                });
                return;
            }

            // Policy 1: expired calibration locks the tool.
            if (item.CalibrationExpired)
            {
                item.Status = ToolStatus.UnderMaintenance;
                c.Update(item);
                Audit(c, itemId, me, "Calibration lockout", $"Checkout blocked. Calibration expired {item.NextCalibrationDate:dd MMM yyyy}.");
                result = OpResult.Fail("Lockout active: device calibration has expired. Contact the QTP / Safety Officer.");
                return;
            }

            // Reservations: never take a tool someone else has booked.
            var now = DateTime.Now;
            var due = ReturnWindow.DueFor(returnWindow, now);
            var active = c.Table<Reservation>().Where(r => r.ItemId == itemId && r.Status == ReservationStatus.Active).ToList();
            var others = active.Where(r => r.UserId != me.Id && r.ReserveDate.Date <= due.Date).OrderBy(r => r.ReserveDate).FirstOrDefault();
            if (others != null)
            {
                if (others.ReserveDate.Date <= now.Date)
                {
                    result = OpResult.Fail($"Reserved today by {others.UserName}{(string.IsNullOrWhiteSpace(others.Project) ? "" : $" for {others.Project}")}.");
                    return;
                }
                due = others.ReserveDate.Date.AddMinutes(-1); // must be back before the next booking
            }

            var mine = active.FirstOrDefault(r => r.UserId == me.Id && r.ReserveDate.Date == now.Date);
            if (mine != null)
            {
                mine.Status = ReservationStatus.Fulfilled;
                c.Update(mine);
            }

            item.Status = ToolStatus.Borrowed;
            item.CurrentBorrowerId = me.Id;
            item.CurrentBorrowerName = me.FullName;
            item.DueDate = due;
            c.Update(item);

            c.Insert(new LoanRecord
            {
                ItemId = item.ItemId, ItemName = item.Name, UserId = me.Id, BorrowerName = me.FullName,
                BorrowDate = now, DueDate = due, SignatureInitials = initials, SignatureData = signatureData ?? string.Empty
            });
            Audit(c, item.ItemId, me, "Checked out", $"Issued to {me.FullName}. Initials {initials}. Due {due:dd MMM HH:mm}.");
            result = OpResult.Success($"{item.Name} issued to you. Return it by {due:ddd dd MMM, HH:mm}.");
        });
        return result;
    }

    // ------------------------------------------------------------------
    // Return (Policies 2 and 5)
    // ------------------------------------------------------------------
    public async Task<OpResult> ReturnAsync(User user, string itemId, string condition, string notes, string photoPath)
    {
        itemId = NormId(itemId);
        if (itemId.Length == 0) return OpResult.Fail("Choose or enter the tool ID to return.");
        if (!ReturnCondition.All.Contains(condition)) return OpResult.Fail("Select the return condition.");

        var db = await Database.GetAsync();
        var result = OpResult.Fail("Return could not be completed.");
        await db.RunInTransactionAsync(c =>
        {
            var me = c.Find<User>(user.Id);
            if (me == null) { result = OpResult.Fail("Your account was not found."); return; }

            var loan = c.Table<LoanRecord>().Where(l => l.ItemId == itemId && l.ReturnDate == null)
                .OrderByDescending(l => l.Id).FirstOrDefault();
            if (loan == null) { result = OpResult.Fail("No active checkout exists for this tool."); return; }

            // Policy 5: only the registered borrower may return the tool.
            if (loan.UserId != me.Id)
            {
                Audit(c, itemId, me, "Return denied", $"Tool is in custody of {loan.BorrowerName}.");
                result = OpResult.Fail($"Only {loan.BorrowerName} can return this tool.");
                return;
            }

            var item = c.Find<Item>(itemId);
            var now = DateTime.Now;
            var message = "Tool returned.";

            if (item != null)
            {
                // Policy 2: Damaged or Needs Maintenance is quarantined immediately.
                item.Status = condition switch
                {
                    ReturnCondition.Damaged => ToolStatus.Damaged,
                    ReturnCondition.NeedsMaintenance => ToolStatus.UnderMaintenance,
                    ReturnCondition.Lost => ToolStatus.Lost,
                    _ => item.CalibrationExpired ? ToolStatus.UnderMaintenance : ToolStatus.Available
                };
                item.CurrentBorrowerId = null;
                item.CurrentBorrowerName = string.Empty;
                item.DueDate = null;
                c.Update(item);

                message = item.Status switch
                {
                    ToolStatus.Available => $"{item.Name} returned and available again.",
                    ToolStatus.UnderMaintenance when condition == ReturnCondition.Available =>
                        $"{item.Name} returned, but its calibration has expired so it is locked until recalibrated.",
                    _ => $"{item.Name} returned and quarantined as {item.Status}. An administrator must clear it before reissue."
                };
            }

            if (condition == ReturnCondition.Lost)
            {
                me.RiskScore += Policy.LostRiskPoints;
                c.Update(me);
            }

            loan.ReturnDate = now;
            loan.ReturnCondition = condition;
            loan.ReturnNotes = notes?.Trim() ?? string.Empty;
            loan.PhotoPath = photoPath ?? string.Empty;
            c.Update(loan);

            var late = now > loan.DueDate ? " Returned late." : string.Empty;
            Audit(c, itemId, me, "Checked in",
                $"Condition: {condition}.{late}{(string.IsNullOrWhiteSpace(loan.ReturnNotes) ? "" : $" Notes: {loan.ReturnNotes}")}{(string.IsNullOrEmpty(loan.PhotoPath) ? "" : " Photo attached.")}");
            result = OpResult.Success(message);
        });
        return result;
    }

    // ------------------------------------------------------------------
    // Reservations (job-site date booking)
    // ------------------------------------------------------------------
    public async Task<OpResult> ReserveAsync(User user, string itemId, DateTime date, string project)
    {
        itemId = NormId(itemId);
        date = date.Date;
        if (itemId.Length == 0) return OpResult.Fail("Choose a tool to reserve.");
        if (date < DateTime.Today) return OpResult.Fail("Reservation date cannot be in the past.");
        if (string.IsNullOrWhiteSpace(project)) return OpResult.Fail("Enter the project or job site for this reservation.");

        var db = await Database.GetAsync();
        var result = OpResult.Fail("Could not create the reservation.");
        await db.RunInTransactionAsync(c =>
        {
            var me = c.Find<User>(user.Id);
            if (me == null || !me.IsActive) { result = OpResult.Fail("Your account is not active."); return; }

            var item = c.Find<Item>(itemId);
            if (item == null) { result = OpResult.Fail("Tool not found."); return; }
            if (item.Status == ToolStatus.Lost) { result = OpResult.Fail("This tool is marked as Lost and cannot be reserved."); return; }
            if (item.RequiresCalibration && date > item.NextCalibrationDate.Date)
            {
                result = OpResult.Fail($"Calibration expires {item.NextCalibrationDate:dd MMM yyyy}, before the requested date.");
                return;
            }

            var clash = c.Table<Reservation>().Where(r => r.ItemId == itemId && r.Status == ReservationStatus.Active)
                .ToList().FirstOrDefault(r => r.ReserveDate.Date == date);
            if (clash != null)
            {
                result = OpResult.Fail($"Already reserved on {date:dd MMM yyyy} by {clash.UserName}.");
                return;
            }

            c.Insert(new Reservation
            {
                ItemId = itemId, ItemName = item.Name, UserId = me.Id, UserName = me.FullName,
                ReserveDate = date, Project = project.Trim(), Status = ReservationStatus.Active, CreatedAt = DateTime.Now
            });
            Audit(c, itemId, me, "Reserved", $"{item.Name} reserved for {date:dd MMM yyyy}, project: {project.Trim()}.");
            result = OpResult.Success($"{item.Name} reserved for {date:ddd dd MMM yyyy}.");
        });
        return result;
    }

    public async Task<OpResult> CancelReservationAsync(User actor, int reservationId)
    {
        var db = await Database.GetAsync();
        var result = OpResult.Fail("Could not cancel the reservation.");
        await db.RunInTransactionAsync(c =>
        {
            var me = c.Find<User>(actor.Id);
            var r = c.Find<Reservation>(reservationId);
            if (me == null || r == null || r.Status != ReservationStatus.Active)
            {
                result = OpResult.Fail("Reservation is no longer active.");
                return;
            }
            if (me.Role != UserRole.Admin && r.UserId != me.Id)
            {
                result = OpResult.Fail("Only the person who reserved the tool, or an administrator, can cancel it.");
                return;
            }
            r.Status = ReservationStatus.Cancelled;
            c.Update(r);
            Audit(c, r.ItemId, me, "Reservation cancelled", $"{r.ItemName} for {r.ReserveDate:dd MMM yyyy} ({r.UserName}).");
            result = OpResult.Success("Reservation cancelled.");
        });
        return result;
    }

    public async Task<List<Reservation>> GetReservationsAsync(User viewer)
    {
        var db = await Database.GetAsync();
        var list = (await db.Table<Reservation>().Where(r => r.Status == ReservationStatus.Active).ToListAsync())
            .OrderBy(r => r.ReserveDate).ThenBy(r => r.ItemId).ToList();
        foreach (var r in list) r.CanCancel = viewer.Role == UserRole.Admin || r.UserId == viewer.Id;
        return list;
    }
}
