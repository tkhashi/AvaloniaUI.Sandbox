using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace HotkeyTest;

internal sealed class App : Application
{
    private readonly FileLogger _logger;
    private HotkeyCoordinator? _coordinator;

    public App(FileLogger logger)
    {
        _logger = logger;
    }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var overlayWindow = new OverlayWindow();
            overlayWindow.Hide();

            _coordinator = new HotkeyCoordinator(_logger, overlayWindow);
            _coordinator.Start();

            desktop.Exit += (_, _) =>
            {
                _coordinator.Dispose();
                _logger.Info("アプリ終了");
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}