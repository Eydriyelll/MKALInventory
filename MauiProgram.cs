using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using MkalInventoryApp.Services;
using MkalInventoryApp.Views;

namespace MkalInventoryApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { CrashLog.Write("Task", e.Exception); e.SetObserved(); };
#if WINDOWS
        if (Microsoft.UI.Xaml.Application.Current is { } winApp)
        {
            // Log the error and keep the window open instead of closing silently.
            winApp.UnhandledException += (_, e) => { CrashLog.Write("WinUI", e.Exception); e.Handled = true; };
        }
#endif

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // One database connection and one set of services for the whole app.
        builder.Services.AddSingleton<AppDatabase>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<InventoryService>();
        builder.Services.AddSingleton<CirculationService>();
        builder.Services.AddSingleton<ComplianceService>();
        builder.Services.AddSingleton<ServiceHub>();
        builder.Services.AddSingleton<SessionService>();
        builder.Services.AddTransient<LoginPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
