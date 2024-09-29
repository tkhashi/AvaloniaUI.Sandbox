using Avalonia.Controls;

using System;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using SharpHook;
using SharpHook.Logging;
using SharpHook.Native;
using SharpHook.Providers;
using SharpHook.Reactive;
using SharpHook.Reactive.Logging;

namespace GlobalHotkey.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var hook = new SimpleReactiveGlobalHook(TaskPoolScheduler.Default);
        hook.KeyPressed.Subscribe(OnHookEvent);
        hook.KeyReleased.Subscribe(e => OnKeyReleased(e, hook));
        hook.Run();
    }

    private bool _isPressingControl = false;

    private bool _isPressingSpace = false;

    void OnHookEvent(KeyboardHookEventArgs e)
    {
        switch (e.Data.KeyCode)
        {
            case KeyCode.VcLeftControl:
                _isPressingControl = true;
                break;
            case KeyCode.VcSpace:
                _isPressingSpace = true;
                break;
            default:
                break;
        }
        
        if(_isPressingControl && _isPressingSpace)
        {
            Console.WriteLine("hgoe");
        }
    }

    void OnKeyReleased(KeyboardHookEventArgs e, IReactiveGlobalHook hook)
    {
        _isPressingControl = false;
        _isPressingSpace = false;

        if (e.Data.KeyCode == KeyCode.VcQ)
        {
            hook.Dispose();
        }
    }
}