using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SCaDaDashboard;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Kalici temayi ilk pencere olusmadan uygula (kaynak fircalari + harita paleti).
        var s = Services.AppSettings.Load();
        ThemeManager.Apply(s.LightTheme ? AppThemeMode.Light : AppThemeMode.Dark);
        base.OnStartup(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("Dispatcher", e.Exception);
    }

    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        Log("Domain", e.ExceptionObject as Exception);
    }

    private static void Log(string src, Exception? ex)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {src}: {ex}\n\n");
        }
        catch { }
    }
}
