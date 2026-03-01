using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using SharpHook;
using SharpHook.Native;
using SharpHook.Reactive;

namespace NotepadApp;

public partial class App : Application
{
    private IReactiveGlobalHook? hook;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // アプリケーションが終了しないように設定
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // desktop.MainWindow = new MainWindow();
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        // グローバルホットキーの設定
        hook = new SimpleReactiveGlobalHook();
        hook.KeyPressed.Subscribe(OnKeyPressed);
        hook.RunAsync();

        base.OnFrameworkInitializationCompleted();
    }
    private void OnKeyPressed(KeyboardHookEventArgs e)
        {
            // 例: Ctrl + Alt + M が押されたとき
            // if (e.Data.KeyCode == KeyCode.VcM &&
            //     e.Data.KeyCode.HasFlag(ModifierMask.LeftCtrl) &&
            //     e.Data.KeyCode.HasFlag(ModifierMask.LeftAlt))
            // {
            //     ShowMainWindow();
            // }
            if (e.Data.KeyCode == KeyCode.VcLeftControl)
            {
                ShowMainWindow();
            }
        }

        private void ShowMainWindow()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (desktop.MainWindow == null || !desktop.MainWindow.IsVisible)
                {
                    desktop.MainWindow = new MainWindow();
                    desktop.MainWindow.Topmost = true;
                    desktop.MainWindow.Show();
                }
                else
                {
                    // ウィンドウが既に表示されている場合はアクティブ化
                    desktop.MainWindow.Activate();
                }
            }
        }
}