using Avalonia;
using System;
using System.Linq;
using AccessibilityApi検証App.Services;

namespace AccessibilityApi検証App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppLogger.Initialize();
        AppLogger.Info("Application startup begin.");

        if (args.Any(a => string.Equals(a, "--self-check", StringComparison.OrdinalIgnoreCase)))
        {
            RunSelfCheck();
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            AppLogger.Info("Application exited normally.");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Unhandled exception in Main.");
            throw;
        }
    }

    private static void RunSelfCheck()
    {
        AppLogger.Info("Self-check mode start.");

        try
        {
            var service = new MacAccessibilityService();
            var windows = service.GetOpenWindows();
            AppLogger.Info($"Self-check windows count={windows.Count}");

            if (windows.Count > 0)
            {
                var first = windows[0];
                var elements = service.GetAxElements(first);
                AppLogger.Info($"Self-check first window pid={first.ProcessId}, elements={elements.Count}");
            }

            Console.WriteLine($"Self-check completed. log={AppLogger.LogFilePath}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Self-check failed.");
            Console.WriteLine($"Self-check failed. {ex.Message}. log={AppLogger.LogFilePath}");
        }

        AppLogger.Info("Self-check mode end.");
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
