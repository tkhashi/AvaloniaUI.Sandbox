using Avalonia.Controls;

namespace TrayIconSample.Views;

public partial class TransparentWindow : Window
{
    public TransparentWindow()
    {
        InitializeComponent();
    }
    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // 左クリックでウィンドウをドラッグ可能にする
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}