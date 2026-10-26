using SQLite;
using MkalInventoryApp.Models;

namespace MkalInventoryApp.Services;

/// <summary>
/// Owns the single SQLite connection (lifecycle, schema, seed data) and writes the tamper-evident audit chain.
/// Every service reaches the database through this class.
/// </summary>
public class AppDatabase
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SQLiteAsyncConnection? _db;

    public AppDatabase(string? path = null)
    {
        // v3: the schema gained initials, job titles and the audit hash chain.
        _path = path ?? Path.Combine(FileSystem.AppDataDirectory, "mkal_inventory_v3.db");
    }

    public async Task<SQLiteAsyncConnection> GetAsync()
    {
        if (_db != null) return _db;
        await _lock.WaitAsync();
        try
        {
            if (_db != null) return _db;

            var db = new SQLiteAsyncConnection(_path);
            await db.CreateTableAsync<User>();
            await db.CreateTableAsync<Item>();
            await db.CreateTableAsync<LoanRecord>();
            await db.CreateTableAsync<Reservation>();
            await db.CreateTableAsync<AuditLog>();
            await SeedAsync(db);
            _db = db;
            return db;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Opens the database in the background so the first sign-in is instant.</summary>
    public Task WarmUpAsync() => GetAsync();

    // ------------------------------------------------------------------
    // Audit chain
    // ------------------------------------------------------------------
    internal static string ComputeHash(AuditLog e) =>
        SecurityHelper.Sha256Hex($"{e.PrevHash}|{e.Timestamp.Ticks}|{e.ItemId}|{e.UserId}|{e.UserName}|{e.Action}|{e.Details}");

    /// <summary>Appends one entry, linked to the previous entry's hash. Must run inside a transaction.</summary>
    internal static void WriteAudit(SQLiteConnection c, string itemId, User? actor, string action, string details)
    {
        var last = c.Table<AuditLog>().OrderByDescending(a => a.Id).FirstOrDefault();
        var entry = new AuditLog
        {
            Timestamp = DateTime.Now,
            ItemId = itemId ?? string.Empty,
            UserId = actor?.Id ?? 0,
            UserName = actor?.FullName ?? "System",
            Action = action,
            Details = details,
            PrevHash = last?.Hash ?? "GENESIS"
        };
        entry.Hash = ComputeHash(entry);
        c.Insert(entry);
    }

    // ------------------------------------------------------------------
    // Seed data (first run only)
    // ------------------------------------------------------------------
    private static async Task SeedAsync(SQLiteAsyncConnection db)
    {
        if (await db.Table<User>().CountAsync() > 0) return;

        static User MakeUser(string username, string name, string role, string title, string initials, string password, string pin)
        {
            var (ph, ps) = SecurityHelper.Hash(password);
            var (nh, ns) = SecurityHelper.Hash(pin);
            return new User
            {
                Username = username, FullName = name, Role = role, JobTitle = title, Initials = initials,
                PasswordHash = ph, PasswordSalt = ps, PinHash = nh, PinSalt = ns, CreatedAt = DateTime.Now
            };
        }

        // Hashing is deliberately slow, so keep it off the UI thread.
        var users = await Task.Run(() => new[]
        {
            MakeUser("admin", "MKAL Admin", UserRole.Admin, JobTitles.All[0], "MA", "Admin@123", "9999"),
            MakeUser("technician", "Field Electrician", UserRole.User, "Journeyman", "FE", "Tech@123", "1234"),
            MakeUser("foreman", "Site Foreman", UserRole.User, "Foreman", "SF", "Tech@123", "4321")
        });

        var today = DateTime.Today;
        Item Tool(string id, string name, string desc, string cat, string serial, bool cal, int days) => new()
        {
            ItemId = id, Name = name, Description = desc, Category = cat, SerialNumber = serial,
            RequiresCalibration = cal, NextCalibrationDate = today.AddDays(days),
            Status = ToolStatus.Available, DateAdded = DateTime.Now
        };

        var items = new[]
        {
            Tool("ISO-001", "Megohmmeter", "Insulation resistance tester", "Testing & Diagnostics", "MG-55012", true, 90),
            Tool("MTR-002", "Digital Multimeter", "True-RMS 600V CAT III", "Testing & Diagnostics", "DM-20871", true, 45),
            Tool("BND-003", "Hydraulic Conduit Bender", "1/2 inch to 2 inch", "Conduit & Pulling", "HB-9034", false, 365),
            Tool("MTR-004", "Clamp Meter", "AC/DC clamp meter, 1000A", "Testing & Diagnostics", "CM-77310", true, -10), // expired: demonstrates the lockout
            Tool("VLT-005", "Voltage Tester", "Non-contact and contact tester", "Testing & Diagnostics", "VT-31457", true, 12), // due soon
            Tool("PUL-006", "Wire Puller", "Electric cable pulling winch", "Conduit & Pulling", "WP-1180", false, 365),
            Tool("DRL-007", "Hammer Drill", "SDS+ rotary hammer", "Power Tools", "HD-4402", false, 365),
            Tool("LAD-008", "Fiberglass Ladder", "8 ft step ladder, insulated", "Safety & Access", "", false, 365)
        };

        await db.RunInTransactionAsync(c =>
        {
            foreach (var u in users) c.Insert(u);
            foreach (var i in items)
            {
                c.Insert(i);
                WriteAudit(c, i.ItemId, null, "Tool registered", $"{i.Name} added to the catalog.");
            }
        });
    }
}
