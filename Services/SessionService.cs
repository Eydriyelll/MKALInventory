using MkalInventoryApp.Models;

namespace MkalInventoryApp.Services;

public class SessionService
{
    public User? CurrentUser { get; set; }
    public void Clear() => CurrentUser = null;
}
