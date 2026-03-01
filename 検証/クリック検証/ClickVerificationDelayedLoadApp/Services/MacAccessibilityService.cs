using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ClickVerificationDelayedLoadApp.Models;

namespace ClickVerificationDelayedLoadApp.Services;

public sealed class MacAccessibilityService
{
    private const uint KcfStringEncodingUtf8 = 0x08000100;
    private const int MaxAxNodeCount = 4000; 
    private const double AxMessageTimeoutSeconds = 2.0;

    private static IntPtr _kCfBooleanTrue = IntPtr.Zero;

    private static IntPtr GetCfBooleanTrue()
    {
        if (_kCfBooleanTrue == IntPtr.Zero)
        {
            IntPtr handle = NativeMethods.dlopen("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", 1);
            if (handle != IntPtr.Zero)
            {
                IntPtr symbol = NativeMethods.dlsym(handle, "kCFBooleanTrue");
                if (symbol != IntPtr.Zero)
                {
                    _kCfBooleanTrue = Marshal.ReadIntPtr(symbol);
                }
            }
        }
        return _kCfBooleanTrue;
    }

    public IReadOnlyList<WindowInfo> GetOpenWindows()
    {
        AppLogger.Info("GetOpenWindows begin.");

        if (!OperatingSystem.IsMacOS()) return [];

        var windowsRef = NativeMethods.CGWindowListCopyWindowInfo(
            NativeMethods.KcgWindowListOptionOnScreenOnly | NativeMethods.KcgWindowListExcludeDesktopElements,
            0);

        if (windowsRef == IntPtr.Zero) return [];

        try
        {
            var count = NativeMethods.CFArrayGetCount(windowsRef);
            var windows = new List<WindowInfo>((int)Math.Min(count, int.MaxValue));

            for (nint i = 0; i < count; i++)
            {
                var windowDict = NativeMethods.CFArrayGetValueAtIndex(windowsRef, i);
                if (windowDict == IntPtr.Zero) continue;

                var layer = CopyDictionaryInt(windowDict, "kCGWindowLayer");
                if (layer != 0) continue;

                var ownerName = CopyDictionaryString(windowDict, "kCGWindowOwnerName");
                var windowName = CopyDictionaryString(windowDict, "kCGWindowName");
                var pid = CopyDictionaryInt(windowDict, "kCGWindowOwnerPID");
                var (x, y) = CopyWindowPosition(windowDict);

                if (pid <= 0 || string.IsNullOrWhiteSpace(ownerName)) continue;

                windows.Add(new WindowInfo(ownerName, windowName, pid, x, y));
            }

            return windows
                .DistinctBy(w => (w.ProcessId, w.WindowTitle, Math.Round(w.X, 2), Math.Round(w.Y, 2)))
                .OrderBy(w => w.AppName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            NativeMethods.CFRelease(windowsRef);
        }
    }

    public IReadOnlyList<AxElementInfo> GetAxElements(WindowInfo window)
    {
        AppLogger.Info($"GetAxElements begin. pid={window.ProcessId}, app={window.AppName}");

        if (!OperatingSystem.IsMacOS()) return [];
        if (!NativeMethods.AXIsProcessTrusted()) throw new InvalidOperationException("アクセシビリティ権限がありません。");

        var appElement = NativeMethods.AXUIElementCreateApplication(window.ProcessId);
        if (appElement == IntPtr.Zero) return [];

        try
        {
            NativeMethods.AXUIElementSetMessagingTimeout(appElement, AxMessageTimeoutSeconds);

            // AXEnhancedUserInterface を安全に設定
            var trueValue = GetCfBooleanTrue();
            if (trueValue != IntPtr.Zero)
            {
                var enhancedKey = CreateCfString("AXEnhancedUserInterface");
                NativeMethods.AXUIElementSetAttributeValue(appElement, enhancedKey, trueValue);
                NativeMethods.CFRelease(enhancedKey);
            }

            var targetWindowElement = FindTargetWindowElement(appElement, window);
            if (targetWindowElement == IntPtr.Zero) return [];

            return EnumerateAxElements(targetWindowElement);
        }
        finally
        {
            NativeMethods.CFRelease(appElement);
        }
    }

    public async Task<DelayedLoadVerificationResult> VerifyDelayedLoadAsync(
        WindowInfo window,
        int attempts,
        TimeSpan interval,
        TimeSpan perAttemptTimeout)
    {
        if (attempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts), "attempts は 1 以上を指定してください。");
        }

        if (interval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "interval は 0 以上を指定してください。");
        }

        if (perAttemptTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(perAttemptTimeout), "perAttemptTimeout は 0 より大きい値を指定してください。");
        }

        AppLogger.Info(
            $"VerifyDelayedLoadAsync start. pid={window.ProcessId}, app={window.AppName}, attempts={attempts}, intervalMs={interval.TotalMilliseconds:0}, timeoutMs={perAttemptTimeout.TotalMilliseconds:0}");

        var started = DateTimeOffset.UtcNow;
        var attemptResults = new List<DelayedLoadAttemptResult>(attempts);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var elapsedMs = (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

            try
            {
                var elementsTask = Task.Run(() => GetAxElements(window));
                var elements = await elementsTask.WaitAsync(perAttemptTimeout);

                var webAreaCount = elements.Count(element => string.Equals(element.Role, "AXWebArea", StringComparison.Ordinal));
                var uniqueSignatureCount = elements
                    .Select(ToElementSignature)
                    .Distinct(StringComparer.Ordinal)
                    .Count();

                var attemptResult = new DelayedLoadAttemptResult(
                    attempt,
                    elapsedMs,
                    elements.Count,
                    webAreaCount,
                    uniqueSignatureCount,
                    false,
                    "");

                attemptResults.Add(attemptResult);

                AppLogger.Info(
                    $"VerifyDelayedLoadAsync attempt={attempt}/{attempts}, elapsedMs={elapsedMs}, elements={elements.Count}, webAreas={webAreaCount}, unique={uniqueSignatureCount}");
            }
            catch (TimeoutException)
            {
                attemptResults.Add(new DelayedLoadAttemptResult(attempt, elapsedMs, 0, 0, 0, true, "Timeout"));
                AppLogger.Error($"VerifyDelayedLoadAsync attempt={attempt}/{attempts} timeout.");
            }
            catch (Exception ex)
            {
                attemptResults.Add(new DelayedLoadAttemptResult(attempt, elapsedMs, 0, 0, 0, false, ex.Message));
                AppLogger.Error(ex, $"VerifyDelayedLoadAsync attempt={attempt}/{attempts} failed.");
            }

            if (attempt < attempts && interval > TimeSpan.Zero)
            {
                await Task.Delay(interval);
            }
        }

        var result = BuildVerdict(window, attemptResults);
        AppLogger.Info(
            $"VerifyDelayedLoadAsync end. delayedLikely={result.IsLikelyDelayedLoad}, reason={result.VerdictReason}, first={result.FirstElementCount}, last={result.LastElementCount}, max={result.MaxElementCount}");
        return result;
    }

    public void MoveMouse(AxElementInfo element)
    {
        if (!element.X.HasValue || !element.Y.HasValue || !element.Width.HasValue || !element.Height.HasValue) return;
        var point = new NativeMethods.CgPoint { X = element.X.Value + element.Width.Value / 2, Y = element.Y.Value + element.Height.Value / 2 };
        AppLogger.Info($"MoveMouse to ({point.X}, {point.Y})");
        PostMouseEvent(NativeMethods.KcgEventMouseMoved, point, 0, 0);
    }

    public void LeftClick(AxElementInfo element) => Click(element, NativeMethods.KcgMouseButtonLeft, 1);
    public void RightClick(AxElementInfo element) => Click(element, NativeMethods.KcgMouseButtonRight, 1);
    public void DoubleLeftClick(AxElementInfo element) => Click(element, NativeMethods.KcgMouseButtonLeft, 2);

    private void Click(AxElementInfo element, uint button, int count)
    {
        if (!element.X.HasValue || !element.Y.HasValue || !element.Width.HasValue || !element.Height.HasValue) return;
        var point = new NativeMethods.CgPoint { X = element.X.Value + element.Width.Value / 2, Y = element.Y.Value + element.Height.Value / 2 };
        
        uint downType = (button == NativeMethods.KcgMouseButtonLeft) ? NativeMethods.KcgEventLeftMouseDown : NativeMethods.KcgEventRightMouseDown;
        uint upType = (button == NativeMethods.KcgMouseButtonLeft) ? NativeMethods.KcgEventLeftMouseUp : NativeMethods.KcgEventRightMouseUp;

        PostMouseEvent(NativeMethods.KcgEventMouseMoved, point, 0, 0);
        for (int i = 1; i <= count; i++)
        {
            PostMouseEvent(downType, point, button, i);
            PostMouseEvent(upType, point, button, i);
            if (i < count) Thread.Sleep(10);
        }
    }

    private static void PostMouseEvent(uint type, NativeMethods.CgPoint point, uint button, int clickState)
    {
        var ev = NativeMethods.CGEventCreateMouseEvent(IntPtr.Zero, type, point, button);
        if (ev != IntPtr.Zero)
        {
            if (clickState > 0) NativeMethods.CGEventSetIntegerValueField(ev, NativeMethods.KcgMouseEventClickState, clickState);
            NativeMethods.CGEventPost(NativeMethods.KcgHidEventTap, ev);
            NativeMethods.CFRelease(ev);
        }
    }

    private static IntPtr FindTargetWindowElement(IntPtr appElement, WindowInfo target)
    {
        var windows = CopyAttributeArray(appElement, "AXWindows");
        foreach (var windowElement in windows)
        {
            var title = CopyAttributeString(windowElement, "AXTitle");
            var (x, y) = CopyAttributePosition(windowElement, "AXPosition");
            if (string.Equals(title, target.WindowTitle, StringComparison.Ordinal) && Math.Abs(x - target.X) < 50 && Math.Abs(y - target.Y) < 50)
            {
                foreach (var item in windows) if (item != windowElement) NativeMethods.CFRelease(item);
                return windowElement;
            }
        }
        var focused = CopyAttributeElement(appElement, "AXFocusedWindow");
        if (focused != IntPtr.Zero) { foreach (var item in windows) NativeMethods.CFRelease(item); return focused; }
        if (windows.Count > 0) { var first = windows[0]; for (int i = 1; i < windows.Count; i++) NativeMethods.CFRelease(windows[i]); return first; }
        return IntPtr.Zero;
    }

    private static IReadOnlyList<AxElementInfo> EnumerateAxElements(IntPtr root)
    {
        var results = new List<AxElementInfo>();
        var queue = new Queue<(IntPtr Element, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0 && results.Count < MaxAxNodeCount)
        {
            var (element, depth) = queue.Dequeue();
            try
            {
                var role = CopyAttributeString(element, "AXRole");
                var subrole = CopyAttributeString(element, "AXSubrole");
                var title = CopyAttributeString(element, "AXTitle");
                if (string.IsNullOrWhiteSpace(title)) title = CopyAttributeString(element, "AXDescription");
                if (role == "AXWebArea") AppLogger.Info($"Found AXWebArea at depth {depth}");

                var (px, py) = CopyAttributePositionNullable(element, "AXPosition");
                var (w, h) = CopyAttributeSizeNullable(element, "AXSize");

                results.Add(new AxElementInfo(depth, role ?? "", subrole ?? "", title ?? "", CopyActionNames(element), px, py, w, h));
                
                var children = CopyAttributeArray(element, "AXChildren");
                foreach (var child in children) queue.Enqueue((child, depth + 1));
            }
            finally { NativeMethods.CFRelease(element); }
        }
        return results;
    }

    private static List<IntPtr> CopyAttributeArray(IntPtr element, string attribute)
    {
        var attrRef = CreateCfString(attribute);
        try
        {
            if (NativeMethods.AXUIElementCopyAttributeValue(element, attrRef, out var valRef) == NativeMethods.AXError.Success && valRef != IntPtr.Zero)
            {
                var count = NativeMethods.CFArrayGetCount(valRef);
                var res = new List<IntPtr>((int)count);
                for (nint i = 0; i < count; i++)
                {
                    var item = NativeMethods.CFArrayGetValueAtIndex(valRef, i);
                    if (item != IntPtr.Zero) { NativeMethods.CFRetain(item); res.Add(item); }
                }
                NativeMethods.CFRelease(valRef);
                return res;
            }
            return [];
        }
        finally { NativeMethods.CFRelease(attrRef); }
    }

    private static IntPtr CopyAttributeElement(IntPtr element, string attribute)
    {
        var attrRef = CreateCfString(attribute);
        try { return NativeMethods.AXUIElementCopyAttributeValue(element, attrRef, out var valRef) == NativeMethods.AXError.Success ? valRef : IntPtr.Zero; }
        finally { NativeMethods.CFRelease(attrRef); }
    }

    private static string CopyAttributeString(IntPtr element, string attribute)
    {
        var attrRef = CreateCfString(attribute);
        try
        {
            if (NativeMethods.AXUIElementCopyAttributeValue(element, attrRef, out var valRef) == NativeMethods.AXError.Success && valRef != IntPtr.Zero)
            {
                var s = CfStringToManaged(valRef);
                NativeMethods.CFRelease(valRef);
                return s;
            }
            return "";
        }
        finally { NativeMethods.CFRelease(attrRef); }
    }

    private static (double X, double Y) CopyAttributePosition(IntPtr element, string attribute)
    {
        var (x, y) = CopyAttributePositionNullable(element, attribute);
        return (x ?? 0, y ?? 0);
    }

    private static (double? X, double? Y) CopyAttributePositionNullable(IntPtr element, string attribute)
    {
        var attrRef = CreateCfString(attribute);
        try
        {
            if (NativeMethods.AXUIElementCopyAttributeValue(element, attrRef, out var valRef) == NativeMethods.AXError.Success && valRef != IntPtr.Zero)
            {
                var pt = new NativeMethods.CgPoint();
                var ok = NativeMethods.AXValueGetValue(valRef, NativeMethods.AXValueType.CgPoint, ref pt);
                NativeMethods.CFRelease(valRef);
                return ok ? (pt.X, pt.Y) : (null, null);
            }
            return (null, null);
        }
        finally { NativeMethods.CFRelease(attrRef); }
    }

    private static (double? W, double? H) CopyAttributeSizeNullable(IntPtr element, string attribute)
    {
        var attrRef = CreateCfString(attribute);
        try
        {
            if (NativeMethods.AXUIElementCopyAttributeValue(element, attrRef, out var valRef) == NativeMethods.AXError.Success && valRef != IntPtr.Zero)
            {
                var sz = new NativeMethods.CgSize();
                var ok = NativeMethods.AXValueGetValue(valRef, NativeMethods.AXValueType.CgSize, ref sz);
                NativeMethods.CFRelease(valRef);
                return ok ? (sz.Width, sz.Height) : (null, null);
            }
            return (null, null);
        }
        finally { NativeMethods.CFRelease(attrRef); }
    }

    private static string CopyActionNames(IntPtr element)
    {
        if (NativeMethods.AXUIElementCopyActionNames(element, out var actRef) == NativeMethods.AXError.Success && actRef != IntPtr.Zero)
        {
            var count = NativeMethods.CFArrayGetCount(actRef);
            var list = new List<string>();
            for (nint i = 0; i < count; i++)
            {
                var item = NativeMethods.CFArrayGetValueAtIndex(actRef, i);
                if (item != IntPtr.Zero) list.Add(CfStringToManaged(item));
            }
            NativeMethods.CFRelease(actRef);
            return string.Join(", ", list);
        }
        return "";
    }

    private static string CopyDictionaryString(IntPtr dict, string key)
    {
        var k = CreateCfString(key);
        try
        {
            var v = NativeMethods.CFDictionaryGetValue(dict, k);
            return (v != IntPtr.Zero && NativeMethods.CFGetTypeID(v) == NativeMethods.CFStringGetTypeID()) ? CfStringToManaged(v) : "";
        }
        finally { NativeMethods.CFRelease(k); }
    }

    private static int CopyDictionaryInt(IntPtr dict, string key)
    {
        var k = CreateCfString(key);
        try
        {
            var v = NativeMethods.CFDictionaryGetValue(dict, k);
            if (v != IntPtr.Zero && NativeMethods.CFGetTypeID(v) == NativeMethods.CFNumberGetTypeID())
            {
                NativeMethods.CFNumberGetValue(v, NativeMethods.CFNumberType.SInt64, out long val);
                return (int)val;
            }
            return 0;
        }
        finally { NativeMethods.CFRelease(k); }
    }

    private static (double X, double Y) CopyWindowPosition(IntPtr dict)
    {
        var k = CreateCfString("kCGWindowBounds");
        try
        {
            var v = NativeMethods.CFDictionaryGetValue(dict, k);
            if (v != IntPtr.Zero && NativeMethods.CFGetTypeID(v) == NativeMethods.CFDictionaryGetTypeID())
            {
                var xk = CreateCfString("X");
                var yk = CreateCfString("Y");
                NativeMethods.CFNumberGetValue(NativeMethods.CFDictionaryGetValue(v, xk), NativeMethods.CFNumberType.Float64, out double x);
                NativeMethods.CFNumberGetValue(NativeMethods.CFDictionaryGetValue(v, yk), NativeMethods.CFNumberType.Float64, out double y);
                NativeMethods.CFRelease(xk); NativeMethods.CFRelease(yk);
                return (x, y);
            }
            return (0, 0);
        }
        finally { NativeMethods.CFRelease(k); }
    }

    private static IntPtr CreateCfString(string s) => NativeMethods.CFStringCreateWithCString(IntPtr.Zero, s, KcfStringEncodingUtf8);

    private static DelayedLoadVerificationResult BuildVerdict(
        WindowInfo window,
        IReadOnlyList<DelayedLoadAttemptResult> attempts)
    {
        var successful = attempts
            .Where(attempt => !attempt.TimedOut && string.IsNullOrWhiteSpace(attempt.ErrorMessage))
            .ToList();

        if (successful.Count == 0)
        {
            return new DelayedLoadVerificationResult(
                window,
                attempts,
                false,
                "全試行が失敗またはタイムアウトしたため判定不能",
                0,
                0,
                0);
        }

        var first = successful[0].TotalElements;
        var last = successful[^1].TotalElements;
        var max = successful.Max(attempt => attempt.TotalElements);
        var uniqueFirst = successful[0].UniqueSignatureCount;
        var uniqueMax = successful.Max(attempt => attempt.UniqueSignatureCount);
        var increase = max - first;
        var uniqueIncrease = uniqueMax - uniqueFirst;
        var increaseRatio = first > 0 ? (double)max / first : 0;
        var lateGrowth = successful.Count >= 2 && successful.Skip(1).Any(attempt => attempt.TotalElements > first);

        if (lateGrowth && increase >= 20 && increaseRatio >= 1.5)
        {
            return new DelayedLoadVerificationResult(
                window,
                attempts,
                true,
                $"要素数が初回 {first} から最大 {max} まで増加 (増分 {increase}, 比率 {increaseRatio:0.00})",
                first,
                last,
                max);
        }

        if (lateGrowth && increase >= 10 && uniqueIncrease >= 10)
        {
            return new DelayedLoadVerificationResult(
                window,
                attempts,
                true,
                $"要素数およびユニーク要素数が時間経過で増加 (要素 +{increase}, ユニーク +{uniqueIncrease})",
                first,
                last,
                max);
        }

        return new DelayedLoadVerificationResult(
            window,
            attempts,
            false,
            $"要素数の増加が限定的 (初回 {first}, 最大 {max}, 最終 {last})",
            first,
            last,
            max);
    }

    private static string ToElementSignature(AxElementInfo element)
    {
        var title = string.IsNullOrWhiteSpace(element.Title)
            ? "(none)"
            : element.Title.Trim();
        return $"{element.Role}|{element.Subrole}|{title}";
    }

    private static string CfStringToManaged(IntPtr cf)
    {
        if (cf == IntPtr.Zero) return "";
        var len = NativeMethods.CFStringGetLength(cf);
        if (len <= 0) return "";
        var size = NativeMethods.CFStringGetMaximumSizeForEncoding(len, KcfStringEncodingUtf8) + 1;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            return NativeMethods.CFStringGetCString(cf, buf, size, KcfStringEncodingUtf8) ? Marshal.PtrToStringUTF8(buf) ?? "" : "";
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static class NativeMethods
    {
        private const string AppServ = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
        private const string CoreFound = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string CoreGraph = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string libSystem = "/usr/lib/libSystem.B.dylib";

        [DllImport(libSystem)] internal static extern IntPtr dlopen(string path, int mode);
        [DllImport(libSystem)] internal static extern IntPtr dlsym(IntPtr handle, string symbol);

        internal const uint KcgWindowListOptionOnScreenOnly = 1;
        internal const uint KcgWindowListExcludeDesktopElements = 16;
        internal const uint KcgEventLeftMouseDown = 1;
        internal const uint KcgEventLeftMouseUp = 2;
        internal const uint KcgEventRightMouseDown = 3;
        internal const uint KcgEventRightMouseUp = 4;
        internal const uint KcgEventMouseMoved = 5;
        internal const uint KcgMouseButtonLeft = 0;
        internal const uint KcgMouseButtonRight = 1;
        internal const uint KcgHidEventTap = 0;
        internal const int KcgMouseEventClickState = 1;

        internal enum AXError { Success = 0 }
        internal enum AXValueType { CgPoint = 1, CgSize = 2 }
        internal enum CFNumberType { SInt64 = 4, Float64 = 13 }

        [StructLayout(LayoutKind.Sequential)] internal struct CgPoint { public double X; public double Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct CgSize { public double Width; public double Height; }

        [DllImport(AppServ)] internal static extern bool AXIsProcessTrusted();
        [DllImport(AppServ)] internal static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint rel);
        [DllImport(AppServ)] internal static extern IntPtr AXUIElementCreateApplication(int pid);
        [DllImport(AppServ)] internal static extern AXError AXUIElementCopyAttributeValue(IntPtr el, IntPtr attr, out IntPtr val);
        [DllImport(AppServ)] internal static extern AXError AXUIElementSetAttributeValue(IntPtr el, IntPtr attr, IntPtr val);
        [DllImport(AppServ)] internal static extern AXError AXUIElementCopyActionNames(IntPtr el, out IntPtr act);
        [DllImport(AppServ)] internal static extern AXError AXUIElementSetMessagingTimeout(IntPtr el, double sec);
        [DllImport(AppServ)] internal static extern bool AXValueGetValue(IntPtr val, AXValueType typ, ref CgPoint pt);
        [DllImport(AppServ)] internal static extern bool AXValueGetValue(IntPtr val, AXValueType typ, ref CgSize sz);

        [DllImport(CoreFound)] internal static extern IntPtr CFStringCreateWithCString(IntPtr al, string s, uint enc);
        [DllImport(CoreFound)] internal static extern nint CFStringGetLength(IntPtr s);
        [DllImport(CoreFound)] internal static extern nint CFStringGetMaximumSizeForEncoding(nint l, uint e);
        [DllImport(CoreFound)] internal static extern bool CFStringGetCString(IntPtr s, IntPtr b, nint sz, uint e);
        [DllImport(CoreFound)] internal static extern nint CFArrayGetCount(IntPtr a);
        [DllImport(CoreFound)] internal static extern IntPtr CFArrayGetValueAtIndex(IntPtr a, nint i);
        [DllImport(CoreFound)] internal static extern IntPtr CFDictionaryGetValue(IntPtr d, IntPtr k);
        [DllImport(CoreFound)] internal static extern ulong CFGetTypeID(IntPtr cf);
        [DllImport(CoreFound)] internal static extern ulong CFStringGetTypeID();
        [DllImport(CoreFound)] internal static extern ulong CFNumberGetTypeID();
        [DllImport(CoreFound)] internal static extern ulong CFDictionaryGetTypeID();
        [DllImport(CoreFound)] internal static extern bool CFNumberGetValue(IntPtr n, CFNumberType t, out long v);
        [DllImport(CoreFound)] internal static extern bool CFNumberGetValue(IntPtr n, CFNumberType t, out double v);
        [DllImport(CoreFound)] internal static extern void CFRelease(IntPtr cf);
        [DllImport(CoreFound)] internal static extern IntPtr CFRetain(IntPtr cf);

        [DllImport(CoreGraph)] internal static extern IntPtr CGEventCreateMouseEvent(IntPtr src, uint typ, CgPoint pt, uint btn);
        [DllImport(CoreGraph)] internal static extern void CGEventPost(uint tap, IntPtr ev);
        [DllImport(CoreGraph)] internal static extern void CGEventSetIntegerValueField(IntPtr ev, int fld, long val);
    }
}
