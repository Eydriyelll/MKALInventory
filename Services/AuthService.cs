using System.Text.RegularExpressions;
using SQLite;
using MkalInventoryApp.Models;

namespace MkalInventoryApp.Services;

/// <summary>
/// Module: Authentication, role security and handover verification.
/// Dual-role sign-in (Admin vs Field Technician), hashed passwords and PINs, and user administration.
/// </summary>
public class AuthService : ServiceBase
{
    public AuthService(AppDatabase database) : base(database) { }

    public async Task<(User? User, string Error)> LoginAsync(string username, string password)
    {
        var db = await Database.GetAsync();
        var name = (username ?? string.Empty).Trim().ToLowerInvariant();
        var user = await db.Table<User>().Where(u => u.Username == name).FirstOrDefaultAsync();

        var valid = user != null &&
                    await Task.Run(() => SecurityHelper.Verify(password ?? string.Empty, user!.PasswordHash, user.PasswordSalt));
        if (!valid)
        {
            if (user != null)
                await db.RunInTransactionAsync(c => Audit(c, string.Empty, null, "Sign-in failed", $"Wrong password for '{user.Username}'."));
            return (null, "Incorrect username or password.");
        }
        if (!user!.IsActive)
            return (null, "This account is deactivated. Ask an administrator to reactivate it.");

        await db.RunInTransactionAsync(c => Audit(c, string.Empty, user, "Sign in", $"{user.RoleText} signed in."));
        return (user, string.Empty);
    }

    public async Task<User?> GetUserAsync(int id)
    {
        var db = await Database.GetAsync();
        return await db.FindAsync<User>(id);
    }

    /// <summary>Policy 3: both the 4-digit PIN and the registered initials must match. Returns an error, or null if verified.</summary>
    internal static string? VerifyHandover(User dbUser, string pin, string initials)
    {
        if (!SecurityHelper.Verify(pin, dbUser.PinHash, dbUser.PinSalt))
            return "Verification failed: incorrect 4-digit PIN.";
        if (!string.Equals(dbUser.Initials, initials, StringComparison.OrdinalIgnoreCase))
            return "Verification failed: sign-off initials do not match the ones registered to your account.";
        return null;
    }

    public static string DeriveInitials(string fullName) =>
        new(fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetter(w[0])).Take(4)
            .Select(w => char.ToUpperInvariant(w[0])).ToArray());

    // ------------------------------------------------------------------
    // User administration (admin only)
    // ------------------------------------------------------------------
    public async Task<List<User>> GetUsersAsync(User actor)
    {
        if (!await IsAdminAsync(actor)) return new List<User>();
        var db = await Database.GetAsync();
        return (await db.Table<User>().ToListAsync()).OrderBy(u => u.Role).ThenBy(u => u.FullName).ToList();
    }

    public async Task<OpResult> AddUserAsync(User actor, string username, string fullName, string role,
        string jobTitle, string? initials, string password, string pin)
    {
        username = (username ?? string.Empty).Trim().ToLowerInvariant();
        fullName = (fullName ?? string.Empty).Trim();
        initials = string.IsNullOrWhiteSpace(initials) ? DeriveInitials(fullName) : initials.Trim().ToUpperInvariant();

        if (!Regex.IsMatch(username, @"^[a-z0-9._]{3,20}$"))
            return OpResult.Fail("Username must be 3 to 20 characters: lowercase letters, numbers, dot or underscore.");
        if (fullName.Length == 0) return OpResult.Fail("Full name is required.");
        if (role != UserRole.Admin && role != UserRole.User) return OpResult.Fail("Choose a role.");
        if (!SecurityHelper.IsValidInitials(initials)) return OpResult.Fail("Initials must be 2 to 4 letters.");
        var pwError = SecurityHelper.ValidatePassword(password);
        if (pwError != null) return OpResult.Fail(pwError);
        if (!SecurityHelper.IsValidPin(pin)) return OpResult.Fail("PIN must be exactly 4 digits.");

        var (ph, ps) = await Task.Run(() => SecurityHelper.Hash(password));
        var (nh, ns) = await Task.Run(() => SecurityHelper.Hash(pin));

        var db = await Database.GetAsync();
        var result = OpResult.Fail("Could not create the user.");
        await db.RunInTransactionAsync(c =>
        {
            if (!IsAdmin(c, actor)) { result = OpResult.AdminOnly; return; }
            if (c.Table<User>().Where(u => u.Username == username).Count() > 0)
            {
                result = OpResult.Fail($"Username '{username}' is already taken.");
                return;
            }
            c.Insert(new User
            {
                Username = username, FullName = fullName, Role = role, JobTitle = jobTitle, Initials = initials!,
                PasswordHash = ph, PasswordSalt = ps, PinHash = nh, PinSalt = ns, CreatedAt = DateTime.Now
            });
            Audit(c, string.Empty, actor, "User created", $"{fullName} ({username}) added as {role}, {jobTitle}, initials {initials}.");
            result = OpResult.Success($"{fullName} can now sign in as {username}. Their sign-off initials are {initials}.");
        });
        return result;
    }

    public async Task<OpResult> ResetPasswordAsync(User actor, int userId, string newPassword)
    {
        var pwError = SecurityHelper.ValidatePassword(newPassword);
        if (pwError != null) return OpResult.Fail(pwError);
        var (hash, salt) = await Task.Run(() => SecurityHelper.Hash(newPassword));

        var db = await Database.GetAsync();
        var result = OpResult.Fail("User not found.");
        await db.RunInTransactionAsync(c =>
        {
            if (!IsAdmin(c, actor)) { result = OpResult.AdminOnly; return; }
            var u = c.Find<User>(userId);
            if (u == null) return;
            u.PasswordHash = hash; u.PasswordSalt = salt;
            c.Update(u);
            Audit(c, string.Empty, actor, "Password reset", $"Password reset for {u.FullName}.");
            result = OpResult.Success($"Password updated for {u.FullName}.");
        });
        return result;
    }

    public async Task<OpResult> ResetPinAsync(User actor, int userId, string newPin)
    {
        if (!SecurityHelper.IsValidPin(newPin)) return OpResult.Fail("PIN must be exactly 4 digits.");
        var (hash, salt) = await Task.Run(() => SecurityHelper.Hash(newPin));

        var db = await Database.GetAsync();
        var result = OpResult.Fail("User not found.");
        await db.RunInTransactionAsync(c =>
        {
            if (!IsAdmin(c, actor)) { result = OpResult.AdminOnly; return; }
            var u = c.Find<User>(userId);
            if (u == null) return;
            u.PinHash = hash; u.PinSalt = salt;
            c.Update(u);
            Audit(c, string.Empty, actor, "PIN reset", $"Handover PIN reset for {u.FullName}.");
            result = OpResult.Success($"Handover PIN updated for {u.FullName}.");
        });
        return result;
    }

    public async Task<OpResult> ChangeOwnPinAsync(User user, string currentPin, string newPin)
    {
        if (!SecurityHelper.IsValidPin(currentPin) || !SecurityHelper.IsValidPin(newPin))
            return OpResult.Fail("Both PINs must be exactly 4 digits.");
        if (currentPin == newPin) return OpResult.Fail("The new PIN must be different from the current one.");

        var (hash, salt) = await Task.Run(() => SecurityHelper.Hash(newPin));
        var db = await Database.GetAsync();
        var result = OpResult.Fail("Account not found.");
        await db.RunInTransactionAsync(c =>
        {
            var u = c.Find<User>(user.Id);
            if (u == null) return;
            if (!SecurityHelper.Verify(currentPin, u.PinHash, u.PinSalt))
            {
                result = OpResult.Fail("Current PIN is incorrect.");
                return;
            }
            u.PinHash = hash; u.PinSalt = salt;
            c.Update(u);
            Audit(c, string.Empty, u, "PIN changed", "Handover PIN changed by its owner.");
            result = OpResult.Success("Your handover PIN was changed.");
        });
        return result;
    }

    public async Task<OpResult> SetActiveAsync(User actor, int userId, bool active)
    {
        var db = await Database.GetAsync();
        var result = OpResult.Fail("User not found.");
        await db.RunInTransactionAsync(c =>
        {
            if (!IsAdmin(c, actor)) { result = OpResult.AdminOnly; return; }
            var u = c.Find<User>(userId);
            if (u == null) return;
            if (!active && u.Id == actor.Id) { result = OpResult.Fail("You cannot deactivate your own account."); return; }
            if (!active && u.Role == UserRole.Admin &&
                c.Table<User>().Where(x => x.Role == UserRole.Admin && x.IsActive).Count() <= 1)
            {
                result = OpResult.Fail("At least one active administrator is required.");
                return;
            }
            if (!active && c.Table<Item>().Where(i => i.CurrentBorrowerId == userId).Count() > 0)
            {
                result = OpResult.Fail($"{u.FullName} still holds tools. Have them returned first.");
                return;
            }
            u.IsActive = active;
            c.Update(u);
            Audit(c, string.Empty, actor, active ? "User activated" : "User deactivated", u.FullName);
            result = OpResult.Success($"{u.FullName} is now {(active ? "active" : "deactivated")}.");
        });
        return result;
    }

    public async Task<OpResult> ResetRiskScoreAsync(User actor, int userId)
    {
        var db = await Database.GetAsync();
        var result = OpResult.Fail("User not found.");
        await db.RunInTransactionAsync(c =>
        {
            if (!IsAdmin(c, actor)) { result = OpResult.AdminOnly; return; }
            var u = c.Find<User>(userId);
            if (u == null) return;
            var old = u.RiskScore;
            u.RiskScore = 0;
            c.Update(u);
            Audit(c, string.Empty, actor, "Risk score reset", $"{u.FullName}: {old} to 0.");
            result = OpResult.Success($"Risk score cleared for {u.FullName}.");
        });
        return result;
    }
}
