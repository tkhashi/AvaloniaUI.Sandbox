using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ClickVerificationDelayedLoadApp.Models;
using ClickVerificationDelayedLoadApp.Services;
using Avalonia.Controls;

namespace ClickVerificationDelayedLoadApp;

public partial class MainWindow : Window
{
    private readonly MacAccessibilityService _accessibilityService = new();
    private readonly ObservableCollection<WindowInfo> _windows = [];
    private readonly ObservableCollection<AxElementInfo> _axElements = [];
    private readonly ObservableCollection<DelayedLoadAttemptResult> _delayLoadResults = [];

    private readonly Button _refreshWindowsButton;
    private readonly Button _loadAxElementsButton;
    private readonly ListBox _windowsListBox;
    private readonly ListBox _axElementsListBox;
    private readonly TextBlock _statusTextBlock;
    private readonly TextBox _delayAttemptCountTextBox;
    private readonly TextBox _delayIntervalMsTextBox;
    private readonly Button _verifyDelayLoadButton;
    private readonly ListBox _delayLoadResultsListBox;

    private readonly Button _moveMouseButton;
    private readonly Button _leftClickButton;
    private readonly Button _rightClickButton;
    private readonly Button _doubleLeftClickButton;

    public MainWindow()
    {
        InitializeComponent();
        AppLogger.Initialize();
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
        _delayAttemptCountTextBox = this.FindControl<TextBox>("DelayAttemptCountTextBox")
            ?? throw new InvalidOperationException("DelayAttemptCountTextBox が見つかりません。");
        _delayIntervalMsTextBox = this.FindControl<TextBox>("DelayIntervalMsTextBox")
            ?? throw new InvalidOperationException("DelayIntervalMsTextBox が見つかりません。");
        _verifyDelayLoadButton = this.FindControl<Button>("VerifyDelayLoadButton")
            ?? throw new InvalidOperationException("VerifyDelayLoadButton が見つかりません。");
        _delayLoadResultsListBox = this.FindControl<ListBox>("DelayLoadResultsListBox")
            ?? throw new InvalidOperationException("DelayLoadResultsListBox が見つかりません。");

        _moveMouseButton = this.FindControl<Button>("MoveMouseButton")
            ?? throw new InvalidOperationException("MoveMouseButton が見つかりません。");
        _leftClickButton = this.FindControl<Button>("LeftClickButton")
            ?? throw new InvalidOperationException("LeftClickButton が見つかりません。");
        _rightClickButton = this.FindControl<Button>("RightClickButton")
            ?? throw new InvalidOperationException("RightClickButton が見つかりません。");
        _doubleLeftClickButton = this.FindControl<Button>("DoubleLeftClickButton")
            ?? throw new InvalidOperationException("DoubleLeftClickButton が見つかりません。");

        _windowsListBox.ItemsSource = _windows;
        _axElementsListBox.ItemsSource = _axElements;
        _delayLoadResultsListBox.ItemsSource = _delayLoadResults;

        _refreshWindowsButton.Click += async (_, _) => await RefreshWindowsAsync();
        _loadAxElementsButton.Click += async (_, _) => await LoadAxElementsForSelectedWindowAsync();
        _verifyDelayLoadButton.Click += async (_, _) => await VerifyDelayLoadForSelectedWindowAsync();
        _windowsListBox.SelectionChanged += async (_, _) => await LoadAxElementsForSelectedWindowAsync();

        _moveMouseButton.Click += (_, _) => PerformClick(service => service.MoveMouse);
        _leftClickButton.Click += (_, _) => PerformClick(service => service.LeftClick);
        _rightClickButton.Click += (_, _) => PerformClick(service => service.RightClick);
        _doubleLeftClickButton.Click += (_, _) => PerformClick(service => service.DoubleLeftClick);

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

    private async Task VerifyDelayLoadForSelectedWindowAsync()
    {
        if (_windowsListBox.SelectedItem is not WindowInfo selectedWindow)
        {
            _statusTextBlock.Text = "検証するウィンドウを選択してください。";
            return;
        }

        if (!TryParsePositiveInt(_delayAttemptCountTextBox.Text, out var attempts))
        {
            _statusTextBlock.Text = "試行回数は 1 以上の整数で入力してください。";
            return;
        }

        if (!TryParseNonNegativeInt(_delayIntervalMsTextBox.Text, out var intervalMs))
        {
            _statusTextBlock.Text = "間隔msは 0 以上の整数で入力してください。";
            return;
        }

        AppLogger.Info(
            $"VerifyDelayLoadForSelectedWindowAsync start. pid={selectedWindow.ProcessId}, app={selectedWindow.AppName}, attempts={attempts}, intervalMs={intervalMs}");
        SetBusyState(true, "遅延ロード検証中...");

        try
        {
            var result = await _accessibilityService.VerifyDelayedLoadAsync(
                selectedWindow,
                attempts,
                TimeSpan.FromMilliseconds(intervalMs),
                TimeSpan.FromSeconds(8));

            _delayLoadResults.Clear();
            foreach (var attempt in result.Attempts)
            {
                _delayLoadResults.Add(attempt);
            }

            var verdictLabel = result.IsLikelyDelayedLoad
                ? "遅延ロードの可能性あり"
                : "遅延ロードが主要因ではない可能性が高い";
            _statusTextBlock.Text = $"{verdictLabel}: {result.VerdictReason}";

            AppLogger.Info(
                $"VerifyDelayLoadForSelectedWindowAsync end. delayedLikely={result.IsLikelyDelayedLoad}, reason={result.VerdictReason}");
        }
        catch (Exception ex)
        {
            _statusTextBlock.Text = $"遅延ロード検証失敗: {ex.Message}";
            AppLogger.Error(ex, "VerifyDelayLoadForSelectedWindowAsync failed.");
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void PerformClick(Func<MacAccessibilityService, Action<AxElementInfo>> clickActionGetter)
    {
        if (_axElementsListBox.SelectedItem is not AxElementInfo selectedElement)
        {
            _statusTextBlock.Text = "操作する要素を選択してください。";
            return;
        }

        try
        {
            var action = clickActionGetter(_accessibilityService);
            action(selectedElement);
            _statusTextBlock.Text = "操作を実行しました。ログを確認してください。";
        }
        catch (Exception ex)
        {
            _statusTextBlock.Text = $"操作失敗: {ex.Message}";
            AppLogger.Error(ex, "PerformClick failed.");
        }
    }

    private void SetBusyState(bool isBusy, string? statusText = null)
    {
        _refreshWindowsButton.IsEnabled = !isBusy;
        _loadAxElementsButton.IsEnabled = !isBusy;
        _verifyDelayLoadButton.IsEnabled = !isBusy;
        _windowsListBox.IsEnabled = !isBusy;
        _delayAttemptCountTextBox.IsEnabled = !isBusy;
        _delayIntervalMsTextBox.IsEnabled = !isBusy;
        _moveMouseButton.IsEnabled = !isBusy;
        _leftClickButton.IsEnabled = !isBusy;
        _rightClickButton.IsEnabled = !isBusy;
        _doubleLeftClickButton.IsEnabled = !isBusy;

        if (!string.IsNullOrWhiteSpace(statusText))
        {
            _statusTextBlock.Text = statusText;
        }
    }

    private static bool TryParsePositiveInt(string? text, out int value)
    {
        if (int.TryParse(text, out value))
        {
            return value > 0;
        }

        value = 0;
        return false;
    }

    private static bool TryParseNonNegativeInt(string? text, out int value)
    {
        if (int.TryParse(text, out value))
        {
            return value >= 0;
        }

        value = 0;
        return false;
    }
}
