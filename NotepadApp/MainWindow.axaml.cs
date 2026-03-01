using Avalonia.Controls;
using Avalonia.Input;

namespace NotepadApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Escキーでウィンドウを非表示にするイベントハンドラを登録
        this.AddHandler(KeyDownEvent, OnKeyDown, handledEventsToo: true);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Hide();
        }
    }
}