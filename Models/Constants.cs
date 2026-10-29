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
    public const string CustomDate = "Pick a date on the calendar";

    public static readonly string[] All = { EndOfShift, Tomorrow, ProjectPhase, CustomDate };

    /// <summary>A date chosen on the calendar is due at the end of that day's shift.</summary>
    public static DateTime DueForDate(DateTime date) => date.Date.AddHours(Policy.ShiftEndHour);

    /// <summary>Returns an error text when the chosen calendar date cannot be used, or null when it is fine.</summary>
    public static string? ValidateCustomDate(DateTime? date, DateTime now)
    {
        if (date == null) return "Choose the return date on the calendar.";
        if (DueForDate(date.Value) <= now)
            return "The return date must be later than now. For today, choose \"End of shift today\".";
        if (date.Value.Date > now.Date.AddDays(Policy.MaxLoanDays))
            return $"A tool can be kept for at most {Policy.MaxLoanDays} days. For a longer project, return it and check it out again.";
        return null;
    }

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
    public const int MaxLoanDays = 90;
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
