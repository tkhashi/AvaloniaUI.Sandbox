using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using AccessibilityApi検証App.Models;
using AccessibilityApi検証App.Services;
using Avalonia.Controls;

namespace AccessibilityApi検証App;

public partial class MainWindow : Window
{
    private readonly MacAccessibilityService _accessibilityService = new();
    private readonly ObservableCollection<WindowInfo> _windows = [];
    private readonly ObservableCollection<AxElementInfo> _axElements = [];

    private readonly Button _refreshWindowsButton;
    private readonly Button _loadAxElementsButton;
    private readonly ListBox _windowsListBox;
    private readonly ListBox _axElementsListBox;
    private readonly TextBlock _statusTextBlock;

    public MainWindow()
    {
        InitializeComponent();
        AppLogger.Info("MainWindow initializing.");

        _refreshWindowsButton = this.FindControl<Button>("RefreshWindowsButton")
            ?? throw new InvalidOperationException("RefreshWindowsButton が見つかりません。");
        _loadAxElementsButton = this.FindControl<Button>("LoadAxElementsButton")
            ?? throw new InvalidOperationException("LoadAxElementsButton が見つかりません。");
        _windowsListBox = this.FindControl<ListBox>("WindowsListBox")
            ?? throw new InvalidOperationException("WindowsListBox が見つかりません。");
        _axElementsListBox = this.FindControl<ListBox>("AxElementsListBox")
            ?? throw new InvalidOperationException("AxElementsListBox が見つかりません。");
        _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock")
            ?? throw new InvalidOperationException("StatusTextBlock が見つかりません。");

        _windowsListBox.ItemsSource = _windows;
        _axElementsListBox.ItemsSource = _axElements;

        _refreshWindowsButton.Click += async (_, _) => await RefreshWindowsAsync();
        _loadAxElementsButton.Click += async (_, _) => await LoadAxElementsForSelectedWindowAsync();
        _windowsListBox.SelectionChanged += async (_, _) => await LoadAxElementsForSelectedWindowAsync();

        Opened += async (_, _) =>
        {
            AppLogger.Info("MainWindow opened.");
            await RefreshWindowsAsync();
        };

        AppLogger.Info($"MainWindow initialized. log={AppLogger.LogFilePath}");
    }

    private async Task RefreshWindowsAsync()
    {
        AppLogger.Info("RefreshWindowsAsync start.");
        SetBusyState(true, "ウィンドウ一覧を取得中...");

        try
        {
            var windows = await Task.Run(_accessibilityService.GetOpenWindows);

            _windows.Clear();
            foreach (var window in windows)
            {
                _windows.Add(window);
            }

            _axElements.Clear();
            _statusTextBlock.Text = $"ウィンドウ数: {_windows.Count}";
            AppLogger.Info($"RefreshWindowsAsync success. windows={_windows.Count}");
        }
        catch (Exception ex)
        {
            _statusTextBlock.Text = $"取得失敗: {ex.Message}";
            AppLogger.Error(ex, "RefreshWindowsAsync failed.");
        }
        finally
        {
            SetBusyState(false);
            AppLogger.Info("RefreshWindowsAsync end.");
        }
    }

    private async Task LoadAxElementsForSelectedWindowAsync()
    {
        if (_windowsListBox.SelectedItem is not WindowInfo selectedWindow)
        {
            _axElements.Clear();
            AppLogger.Info("LoadAxElementsForSelectedWindowAsync skipped. no selection.");
            return;
        }

        AppLogger.Info($"LoadAxElementsForSelectedWindowAsync start. pid={selectedWindow.ProcessId}, app={selectedWindow.AppName}");
        SetBusyState(true, "AX要素を取得中...");

        try
        {
            var elementsTask = Task.Run(() => _accessibilityService.GetAxElements(selectedWindow));
            var elements = await elementsTask.WaitAsync(TimeSpan.FromSeconds(8));

            _axElements.Clear();
            foreach (var element in elements)
            {
                _axElements.Add(element);
            }

            _statusTextBlock.Text = $"AX要素数: {_axElements.Count}";
            AppLogger.Info($"LoadAxElementsForSelectedWindowAsync success. elements={_axElements.Count}");
        }
        catch (Exception ex)
        {
            _statusTextBlock.Text = $"AX要素取得失敗: {ex.Message}";
            AppLogger.Error(ex, "LoadAxElementsForSelectedWindowAsync failed.");
        }
        finally
        {
            SetBusyState(false);
            AppLogger.Info("LoadAxElementsForSelectedWindowAsync end.");
        }
    }

    private void SetBusyState(bool isBusy, string? statusText = null)
    {
        _refreshWindowsButton.IsEnabled = !isBusy;
        _loadAxElementsButton.IsEnabled = !isBusy;
        _windowsListBox.IsEnabled = !isBusy;

        if (!string.IsNullOrWhiteSpace(statusText))
        {
            _statusTextBlock.Text = statusText;
        }
    }
}