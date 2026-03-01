using System;
using Avalonia.Controls;
using ToolbarIconUiVerificationApp.Services;

namespace ToolbarIconUiVerificationApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var hideButton = this.FindControl<Button>("HideWindowButton")
            ?? throw new InvalidOperationException("HideWindowButton が見つかりません。");
        hideButton.Click += (_, _) =>
        {
            Hide();
            AppLogger.Info("GUIウィンドウ: ボタンで非表示");
        };
    }
}
