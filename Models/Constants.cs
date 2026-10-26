namespace MkalInventoryApp.Models;

public static class ToolStatus
{
    public const string Available = "Available";
    public const string Borrowed = "Borrowed";
    public const string UnderMaintenance = "Under Maintenance";
    public const string Damaged = "Damaged";
    public const string Overdue = "Overdue";
    public const string Lost = "Lost";

    public static readonly string[] All = { Available, Borrowed, UnderMaintenance, Damaged, Overdue, Lost };
}

public static class ReturnCondition
{
    public const string Available = "Available";
    public const string NeedsMaintenance = "Needs Maintenance";
    public const string Damaged = "Damaged";
    public const string Lost = "Lost";

    public static readonly string[] All = { Available, NeedsMaintenance, Damaged, Lost };
}

public static class UserRole
{
    public const string Admin = "Admin";
    public const string User = "User";
}

public static class JobTitles
{
    public static readonly string[] All = { "QTP / Office Administrator", "Foreman", "Journeyman", "Apprentice" };
}

public static class ReservationStatus
{
    public const string Active = "Active";
    public const string Cancelled = "Cancelled";
    public const string Fulfilled = "Fulfilled";
    public const string Expired = "Expired";
}

public static class ToolCategories
{
    public static readonly string[] All =
    {
        "Testing & Diagnostics", "Hand Tools", "Power Tools",
        "Conduit & Pulling", "Safety & Access", "Other"
    };
}

/// <summary>How long a technician plans to keep a tool (Policy 4: return at the end of the shift or project phase).</summary>
public static class ReturnWindow
{
    public const string EndOfShift = "End of shift today";
    public const string Tomorrow = "Tomorrow's shift";
    public const string ProjectPhase = "3 days (project phase)";

    public static readonly string[] All = { EndOfShift, Tomorrow, ProjectPhase };

    public static DateTime DueFor(string? choice, DateTime now)
    {
        var shiftEnd = now.Date.AddHours(Policy.ShiftEndHour);
        return choice switch
        {
            EndOfShift => now >= shiftEnd ? shiftEnd.AddDays(1) : shiftEnd,
            Tomorrow => shiftEnd.AddDays(1),
            _ => shiftEnd.AddDays(Policy.LoanDays)
        };
    }
}

public static class Policy
{
    public const int ShiftEndHour = 17;
    public const int LoanDays = 3;
    public const int CalibrationWarningDays = 30;
    public const int OverdueRiskPoints = 10;
    public const int LostRiskPoints = 20;

    public static string RiskBand(int score) => score switch
    {
        < 20 => "Low",
        < 50 => "Medium",
        _ => "High"
    };
}
