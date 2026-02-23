namespace HotkeyTest;

internal sealed class HotkeyCoordinator : IDisposable
{
    private readonly FileLogger _logger;
    private readonly OverlayWindow _overlayWindow;
    private readonly MacGlobalHotkeyListener _hotkeyListener;
    private readonly MacWindowInspector _windowInspector;

    public HotkeyCoordinator(FileLogger logger, OverlayWindow overlayWindow)
    {
        _logger = logger;
        _overlayWindow = overlayWindow;
        _windowInspector = new MacWindowInspector();
        _hotkeyListener = new MacGlobalHotkeyListener(_logger, OnHotkeyPressed);
    }

    public void Start()
    {
        _logger.Info("ホットキー監視開始 (Cmd+Shift+M)");
        _hotkeyListener.Start();
    }

    public void Dispose()
    {
        _hotkeyListener.Dispose();
    }

    private void OnHotkeyPressed()
    {
        try
        {
            var context = _windowInspector.TryGetFrontWindowContext();
            if (context is null)
            {
                _logger.Info("ホットキー押下: 前面ウィンドウ取得失敗");
                return;
            }

            var luminance = _windowInspector.TryAnalyzeWindowLuminance(context.Value);
            var result = new HotkeyResult(
                context.Value.ProcessName,
                context.Value.ProcessId,
                context.Value.WindowId,
                context.Value.X,
                context.Value.Y,
                context.Value.Width,
                context.Value.Height,
                luminance,
                ToBrightnessLabel(luminance));

            _logger.Info($"ホットキー押下: process={result.ProcessName}, pid={result.ProcessId}, window={result.WindowId}, x={result.X:0}, y={result.Y:0}, w={result.Width:0}, h={result.Height:0}, luminance={result.AverageLuminance:0.000}");
            _overlayWindow.ShowHotkeyResult(result);
        }
        catch (Exception ex)
        {
            _logger.Info($"ホットキー処理エラー: {ex}");
        }
    }

    private static string ToBrightnessLabel(double value)
    {
        if (value < 0.33)
        {
            return "dark";
        }

        if (value < 0.66)
        {
            return "medium";
        }

        return "bright";
    }
}

internal readonly record struct HotkeyResult(
    string ProcessName,
    int ProcessId,
    uint WindowId,
    double X,
    double Y,
    double Width,
    double Height,
    double AverageLuminance,
    string BrightnessLabel);