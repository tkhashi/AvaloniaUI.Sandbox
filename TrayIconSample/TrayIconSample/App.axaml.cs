using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using TrayIconSample.ViewModels;
using TrayIconSample.Views;

namespace TrayIconSample;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Line below is needed to remove Avalonia data validation.
            // Without this line you will get duplicate validations from both Avalonia and CT
            BindingPlugins.DataValidators.RemoveAt(0);
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
            
            _desktopLifetime = desktop;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
    
    private void OnClickSettings(object sender, EventArgs e)
    {
        var window = new SettingWindow();
        window.Show();
    }
    
    private void OnClickApplication(object sender, EventArgs e)
    {
        var window = new TransparentWindow();
        window.Show();
    }
    
    private void OnClickQuit(object sender, EventArgs e)
    {
        _desktopLifetime?.Shutdown();
    }
}