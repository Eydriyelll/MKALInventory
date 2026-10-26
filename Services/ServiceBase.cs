using SQLite;
using MkalInventoryApp.Models;

namespace MkalInventoryApp.Services;

/// <summary>Common base for the four module services: shared database access and the admin check.</summary>
public abstract class ServiceBase
{
    protected AppDatabase Database { get; }

    protected ServiceBase(AppDatabase database) => Database = database;

    protected static string NormId(string? id) => (id ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Re-reads the actor from the database, so a stale or tampered client copy is never trusted.</summary>
    protected static bool IsAdmin(SQLiteConnection c, User? actor)
    {
        if (actor == null) return false;
        var u = c.Find<User>(actor.Id);
        return u is { IsActive: true, Role: UserRole.Admin };
    }

    protected async Task<bool> IsAdminAsync(User? actor)
    {
        if (actor == null) return false;
        var db = await Database.GetAsync();
        var u = await db.FindAsync<User>(actor.Id);
        return u is { IsActive: true, Role: UserRole.Admin };
    }

    protected static void Audit(SQLiteConnection c, string itemId, User? actor, string action, string details) =>
        AppDatabase.WriteAudit(c, itemId, actor, action, details);
}
