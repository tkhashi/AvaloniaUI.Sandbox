using System.Runtime.InteropServices;
using System.Diagnostics;

namespace HotkeyTest;

internal sealed class MacGlobalHotkeyListener : IDisposable
{
    private readonly FileLogger _logger;
    private readonly Action _onPressed;
    private readonly CGEventTapCallback _eventCallback;
    private readonly object _sync = new();
    private Thread? _thread;
    private IntPtr _runLoop;
    private bool _disposed;
    private DateTimeOffset _lastTriggeredAt = DateTimeOffset.MinValue;
    private int _debugEventLogCount;

    public MacGlobalHotkeyListener(FileLogger logger, Action onPressed)
    {
        _logger = logger;
        _onPressed = onPressed;
        _eventCallback = OnEvent;
    }

    public void Start()
    {
        if (!OperatingSystem.IsMacOS())
        {
            _logger.Info("macOS以外のためホットキー監視は無効です。");
            return;
        }

        RequestPermissions();

        lock (_sync)
        {
            if (_thread is not null)
            {
                return;
            }

            _thread = new Thread(EventTapThreadMain)
            {
                IsBackground = true,
                Name = "mac-hotkey-listener"
            };
            _thread.Start();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_sync)
        {
            if (_runLoop != IntPtr.Zero)
            {
                CFRunLoopStop(_runLoop);
                _runLoop = IntPtr.Zero;
            }
        }
    }

    private void EventTapThreadMain()
    {
        var mask = EventMaskFor(EventType.KeyDown);
        var tap = CGEventTapCreate(
            CGEventTapLocation.Session,
            CGEventTapPlacement.HeadInsert,
            CGEventTapOptions.ListenOnly,
            mask,
            _eventCallback,
            IntPtr.Zero);

        if (tap == IntPtr.Zero)
        {
            _logger.Info("CGEventTapCreateに失敗しました。macOSの『入力監視』権限を許可してください。");
            return;
        }

        _logger.Info("CGEventTapCreate 成功");

        var source = CFMachPortCreateRunLoopSource(IntPtr.Zero, tap, 0);
        if (source == IntPtr.Zero)
        {
            _logger.Info("CFMachPortCreateRunLoopSource に失敗しました。");
            CFRelease(tap);
            return;
        }

        var runLoop = CFRunLoopGetCurrent();
        if (runLoop == IntPtr.Zero)
        {
            _logger.Info("CFRunLoopGetCurrent が IntPtr.Zero を返しました。");
            CFRelease(source);
            CFRelease(tap);
            return;
        }

        _runLoop = runLoop;

        CFRunLoopAddSource(runLoop, source, CFRunLoopModeDefault);
        CGEventTapEnable(tap, true);
        _logger.Info("ホットキーイベントループ開始");

        while (!_disposed)
        {
            var runResult = CFRunLoopRunInMode(CFRunLoopModeDefault, 1.0, false);
            if (runResult is CFRunLoopRunResult.Finished or CFRunLoopRunResult.Stopped)
            {
                _logger.Info($"ホットキーイベントループ停止: {runResult}");
                break;
            }
        }

        _logger.Info("ホットキーイベントループ終了");

        CFRelease(source);
        CFRelease(tap);
    }

    private IntPtr OnEvent(IntPtr proxy, uint eventTypeRaw, IntPtr cgEvent, IntPtr userInfo)
    {
        if ((EventType)eventTypeRaw != EventType.KeyDown)
        {
            return cgEvent;
        }

        var keyCode = CGEventGetIntegerValueField(cgEvent, CGEventField.KeyboardEventKeycode);
        var flags = CGEventGetFlags(cgEvent);

        if (_debugEventLogCount < 30)
        {
            _debugEventLogCount++;
            _logger.Info($"key-event: keyCode={keyCode}, flags=0x{flags:X}, matched={IsDesiredHotkey(keyCode, flags)}");
        }

        if (IsDesiredHotkey(keyCode, flags) && DateTimeOffset.UtcNow - _lastTriggeredAt > TimeSpan.FromMilliseconds(300))
        {
            _lastTriggeredAt = DateTimeOffset.UtcNow;
            _logger.Info("ホットキー条件一致: Cmd+Shift+M");
            _onPressed();
        }

        return cgEvent;
    }

    private static bool IsDesiredHotkey(long keyCode, ulong flags)
    {
        const long mKeyCode = 46;
        const ulong command = 1UL << 20;
        const ulong shift = 1UL << 17;

        var required = command | shift;
        return keyCode == mKeyCode && (flags & required) == required;
    }

    private static ulong EventMaskFor(EventType eventType)
    {
        return 1UL << (int)eventType;
    }

    private void RequestPermissions()
    {
        LogCurrentProcessIdentity();

        try
        {
            var inputGranted = CGPreflightListenEventAccess();
            _logger.Info($"入力監視 権限状態: {(inputGranted ? "granted" : "not-granted")}");
            if (!inputGranted)
            {
                var requested = CGRequestListenEventAccess();
                _logger.Info($"入力監視 権限要求を実行: {(requested ? "accepted" : "pending-or-denied")}");
                OpenPrivacyPane("Privacy_ListenEvent");
            }
        }
        catch (Exception ex)
        {
            _logger.Info($"入力監視 権限要求で例外: {ex.Message}");
        }

        try
        {
            var screenGranted = CGPreflightScreenCaptureAccess();
            _logger.Info($"画面収録 権限状態: {(screenGranted ? "granted" : "not-granted")}");
            if (!screenGranted)
            {
                var requested = CGRequestScreenCaptureAccess();
                _logger.Info($"画面収録 権限要求を実行: {(requested ? "accepted" : "pending-or-denied")}");
                OpenPrivacyPane("Privacy_ScreenCapture");
            }
        }
        catch (Exception ex)
        {
            _logger.Info($"画面収録 権限要求で例外: {ex.Message}");
        }
    }

    private void LogCurrentProcessIdentity()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var processName = process.ProcessName;
            var processPath = Environment.ProcessPath ?? "unknown";
            _logger.Info($"権限対象プロセス: name={processName}, path={processPath}");

            if (processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("dotnet run 実行時は macOS 設定上で『dotnet』または実行元ターミナル（Terminal/Visual Studio Code）を許可対象として確認してください。");
            }
        }
        catch (Exception ex)
        {
            _logger.Info($"プロセス情報の取得に失敗: {ex.Message}");
        }
    }

    private void OpenPrivacyPane(string anchor)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { $"x-apple.systempreferences:com.apple.preference.security?{anchor}" },
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            _logger.Info($"設定画面オープン失敗({anchor}): {ex.Message}");
        }
    }

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int Utf8Encoding = 0x08000100;
    private static readonly IntPtr CFRunLoopModeDefault = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", Utf8Encoding);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventTapCreate(
        CGEventTapLocation tap,
        CGEventTapPlacement place,
        CGEventTapOptions options,
        ulong eventsOfInterest,
        CGEventTapCallback callback,
        IntPtr userInfo);

    [DllImport(CoreGraphics)]
    private static extern void CGEventTapEnable(IntPtr tap, bool enable);

    [DllImport(CoreGraphics)]
    private static extern long CGEventGetIntegerValueField(IntPtr cgEvent, CGEventField field);

    [DllImport(CoreGraphics)]
    private static extern ulong CGEventGetFlags(IntPtr cgEvent);

    [DllImport(CoreGraphics)]
    private static extern bool CGPreflightListenEventAccess();

    [DllImport(CoreGraphics)]
    private static extern bool CGRequestListenEventAccess();

    [DllImport(CoreGraphics)]
    private static extern bool CGPreflightScreenCaptureAccess();

    [DllImport(CoreGraphics)]
    private static extern bool CGRequestScreenCaptureAccess();

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, int order);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFRunLoopGetCurrent();

    [DllImport(CoreFoundation)]
    private static extern void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [DllImport(CoreFoundation)]
    private static extern CFRunLoopRunResult CFRunLoopRunInMode(IntPtr mode, double seconds, bool returnAfterSourceHandled);

    [DllImport(CoreFoundation)]
    private static extern void CFRunLoopStop(IntPtr runLoop);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr obj);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string str, int encoding);

    private enum CGEventTapLocation : uint
    {
        Hid = 0,
        Session = 1,
        AnnotatedSession = 2
    }

    private enum CGEventTapPlacement : uint
    {
        HeadInsert = 0,
        TailAppend = 1
    }

    private enum CGEventTapOptions : uint
    {
        Default = 0,
        ListenOnly = 1
    }

    private enum EventType : int
    {
        KeyDown = 10
    }

    private enum CGEventField : uint
    {
        KeyboardEventKeycode = 9
    }

    private enum CFRunLoopRunResult : int
    {
        Finished = 1,
        Stopped = 2,
        TimedOut = 3,
        HandledSource = 4
    }

    private delegate IntPtr CGEventTapCallback(IntPtr proxy, uint type, IntPtr cgEvent, IntPtr userInfo);
}