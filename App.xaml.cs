using Microsoft.Extensions.DependencyInjection;
using MkalInventoryApp.Views;

namespace MkalInventoryApp;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        UserAppTheme = AppTheme.Light; // the palette is designed for light mode on every device
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var login = _services.GetRequiredService<LoginPage>();
        var window = new Window(new NavigationPage(login)) { Title = "MKAL Inventory" };

#if WINDOWS || MACCATALYST
        // A comfortable desktop size on Windows and Mac.
        window.Width = 1180;
        window.Height = 820;
        window.MinimumWidth = 480;
        window.MinimumHeight = 560;
#endif
        return window;
    }
}
