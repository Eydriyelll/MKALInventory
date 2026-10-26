using MkalInventoryApp.Services;

namespace MkalInventoryApp.Views;

public partial class LoginPage : ContentPage
{
    private const int MaxAttempts = 5;
    private const int LockSeconds = 30;

    private readonly ServiceHub _hub;
    private readonly SessionService _session;
    private int _failed;
    private DateTime _lockedUntil = DateTime.MinValue;

    public LoginPage(ServiceHub hub, SessionService session)
    {
        InitializeComponent();
        _hub = hub;
        _session = session;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _session.Clear();
        txtPassword.Text = string.Empty;
        lblError.IsVisible = false;

        // Open the database now, so the first sign-in is instant.
        try { await _hub.Database.WarmUpAsync(); }
        catch (Exception ex) { CrashLog.Write("Database warm-up", ex); ShowError($"The database could not be opened: {ex.Message}"); }
    }

    private void ShowError(string message)
    {
        lblError.Text = message;
        lblError.IsVisible = true;
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        if (!btnLogin.IsEnabled) return;

        if (DateTime.Now < _lockedUntil)
        {
            var wait = (int)Math.Ceiling((_lockedUntil - DateTime.Now).TotalSeconds);
            ShowError($"Too many failed attempts. Try again in {wait} seconds.");
            return;
        }

        var username = txtUsername.Text?.Trim() ?? string.Empty;
        var password = txtPassword.Text ?? string.Empty;
        if (username.Length == 0 || password.Length == 0)
        {
            ShowError("Enter your username and password.");
            return;
        }

        btnLogin.IsEnabled = false;
        btnLogin.Text = "Signing in...";
        try
        {
            var (user, error) = await _hub.Auth.LoginAsync(username, password);
            if (user == null)
            {
                if (++_failed >= MaxAttempts)
                {
                    _failed = 0;
                    _lockedUntil = DateTime.Now.AddSeconds(LockSeconds);
                    error = $"Too many failed attempts. Sign-in is paused for {LockSeconds} seconds.";
                }
                ShowError(error);
                return;
            }

            _failed = 0;
            lblError.IsVisible = false;
            _session.CurrentUser = user;

            Page next = user.IsAdmin
                ? new AdminDashboardPage(_hub, _session)
                : new UserDashboardPage(_hub, _session);
            await Navigation.PushAsync(next);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Login", ex);
            ShowError($"Something went wrong: {ex.Message}");
        }
        finally
        {
            btnLogin.IsEnabled = true;
            btnLogin.Text = "Sign in";
        }
    }
}
