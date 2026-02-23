using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace HotkeyTest;

internal sealed class OverlayWindow : Window
{
    private readonly TextBlock _textBlock;
    private DispatcherTimer? _hideTimer;

    public OverlayWindow()
    {
        Width = 520;
        Height = 190;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _textBlock = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        };

        Content = new Border
        {
            Padding = new Thickness(14),
            Background = new SolidColorBrush(Color.FromArgb(225, 28, 28, 28)),
            CornerRadius = new CornerRadius(10),
            Child = new StackPanel
            {
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Global Hotkey Result",
                        Foreground = new SolidColorBrush(Color.FromRgb(102, 204, 255)),
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold
                    },
                    _textBlock
                }
            }
        };
    }

    public void ShowHotkeyResult(HotkeyResult result)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var monitorText = ResolveMonitorText(result);
            _textBlock.Text =
                $"Process: {result.ProcessName} (PID={result.ProcessId})\n" +
                $"Handle(WindowId): {result.WindowId}\n" +
                $"Bounds: X={result.X:0}, Y={result.Y:0}, W={result.Width:0}, H={result.Height:0}\n" +
                $"Monitor: {monitorText}\n" +
                $"Analysis: avg-luminance={result.AverageLuminance:0.000} ({result.BrightnessLabel})";

            Position = CalculateOverlayPosition(result);

            if (!IsVisible)
            {
                Show();
            }

            _hideTimer?.Stop();
            _hideTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) =>
            {
                _hideTimer?.Stop();
                Hide();
            });
            _hideTimer.Start();
        });
    }

    private PixelPoint CalculateOverlayPosition(HotkeyResult result)
    {
        var margin = 12;
        var x = (int)Math.Round(result.X + margin);
        var y = (int)Math.Round(result.Y + margin);
        var target = new PixelPoint(x, y);

        var screen = Screens.All.FirstOrDefault(s => s.Bounds.Contains(new PixelPoint((int)(result.X + result.Width / 2), (int)(result.Y + result.Height / 2))))
                     ?? Screens.Primary;

        if (screen is null)
        {
            return target;
        }

        var maxX = screen.Bounds.X + screen.Bounds.Width - (int)Width - margin;
        var maxY = screen.Bounds.Y + screen.Bounds.Height - (int)Height - margin;

        x = Math.Clamp(x, screen.Bounds.X + margin, maxX);
        y = Math.Clamp(y, screen.Bounds.Y + margin, maxY);

        return new PixelPoint(x, y);
    }

    private string ResolveMonitorText(HotkeyResult result)
    {
        var center = new PixelPoint((int)(result.X + result.Width / 2), (int)(result.Y + result.Height / 2));
        var screen = Screens.All.FirstOrDefault(s => s.Bounds.Contains(center)) ?? Screens.Primary;

        if (screen is null)
        {
            return "unknown";
        }

        return $"{screen.Bounds.Width}x{screen.Bounds.Height} @ ({screen.Bounds.X},{screen.Bounds.Y})";
    }
}