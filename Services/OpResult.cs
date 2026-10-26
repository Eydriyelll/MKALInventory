namespace MkalInventoryApp.Services;

/// <summary>Outcome of a service operation: success flag plus a message that is safe to show the user.</summary>
public record OpResult(bool Ok, string Message)
{
    public static OpResult Success(string message = "Done.") => new(true, message);
    public static OpResult Fail(string message) => new(false, message);
    public static OpResult AdminOnly => new(false, "Only an active administrator can do this.");
}
