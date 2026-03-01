using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using ToolbarIconUiVerificationApp.Services;

namespace ToolbarIconUiVerificationApp;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private bool _isShuttingDown;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppLogger.Info("アプリ起動");
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) =>
            {
                _isShuttingDown = true;
                _mainWindow?.Close();
                _trayIcon?.Dispose();
                AppLogger.Info("アプリ終了");
            };

            InitializeTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeTrayIcon()
    {
        try
        {
            _trayIcon = new TrayIcon
            {
                Icon = LoadTrayIcon(),
                ToolTipText = "Toolbar UI Verification",
                IsVisible = true,
                Menu = BuildTrayMenu()
            };

            if (OperatingSystem.IsMacOS())
            {
                MacOSProperties.SetIsTemplateIcon(_trayIcon, true);
            }

            var icons = new TrayIcons();
            icons.Add(_trayIcon);
            TrayIcon.SetIcons(this, icons);
            AppLogger.Info("トレイアイコン初期化完了");
        }
        catch (Exception ex)
        {
            AppLogger.Error("トレイアイコン初期化失敗", ex);
            throw;
        }
    }

    private static WindowIcon LoadTrayIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://ToolbarIconUiVerificationApp/Assets/shark.png"));
        return new WindowIcon(stream);
    }

    private NativeMenu BuildTrayMenu()
    {
        var guiItem = new NativeMenuItem("GUIを表示");
        guiItem.Click += (_, _) =>
        {
            AppLogger.Info("メニュー: GUIを表示");
            ShowMainWindow();
        };

        var closeGuiItem = new NativeMenuItem("GUIを隠す");
        closeGuiItem.Click += (_, _) =>
        {
            AppLogger.Info("メニュー: GUIを隠す");
            _mainWindow?.Hide();
        };

        var exitItem = new NativeMenuItem("終了");
        exitItem.Click += (_, _) =>
        {
            _isShuttingDown = true;
            AppLogger.Info("メニュー: 終了");
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        };

        return new NativeMenu
        {
            Items =
            {
                guiItem,
                closeGuiItem,
                new NativeMenuItemSeparator(),
                exitItem
            }
        };
    }

    private void ShowMainWindow()
    {
        var window = _mainWindow ??= CreateMainWindow();
        if (!window.IsVisible)
        {
            window.Show();
            AppLogger.Info("GUIウィンドウを表示");
        }

        window.Activate();
    }

    private MainWindow CreateMainWindow()
    {
        var window = new MainWindow();
        window.Closing += (_, e) =>
        {
            if (_isShuttingDown)
            {
                return;
            }

            e.Cancel = true;
            window.Hide();
            AppLogger.Info("GUIウィンドウを閉じる操作で非表示化");
        };

        return window;
    }
}
