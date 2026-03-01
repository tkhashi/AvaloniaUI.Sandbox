using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using ClickVerificationApp.Models;

namespace ClickVerificationApp.Services;

public sealed class MacAccessibilityService
{
    private const uint KcfStringEncodingUtf8 = 0x08000100;
    private const int MaxAxNodeCount = 3000;
    private const double AxMessageTimeoutSeconds = 1.0;

    public IReadOnlyList<WindowInfo> GetOpenWindows()
    {
        AppLogger.Info("GetOpenWindows begin.");

        if (!OperatingSystem.IsMacOS())
        {
            AppLogger.Info("GetOpenWindows skipped: non-macOS.");
            return [];
        }

        var windowsRef = NativeMethods.CGWindowListCopyWindowInfo(
            NativeMethods.KcgWindowListOptionOnScreenOnly | NativeMethods.KcgWindowListExcludeDesktopElements,
            0);

        if (windowsRef == IntPtr.Zero)
        {
            AppLogger.Error("GetOpenWindows failed: CGWindowListCopyWindowInfo returned null.");
            return [];
        }

        try
        {
            var count = NativeMethods.CFArrayGetCount(windowsRef);
            var windows = new List<WindowInfo>((int)Math.Min(count, int.MaxValue));

            for (nint i = 0; i < count; i++)
            {
                var windowDict = NativeMethods.CFArrayGetValueAtIndex(windowsRef, i);
                if (windowDict == IntPtr.Zero)
                {
                    continue;
                }

                var layer = CopyDictionaryInt(windowDict, "kCGWindowLayer");
                if (layer != 0)
                {
                    continue;
                }

                var ownerName = CopyDictionaryString(windowDict, "kCGWindowOwnerName");
                var windowName = CopyDictionaryString(windowDict, "kCGWindowName");
                var pid = CopyDictionaryInt(windowDict, "kCGWindowOwnerPID");
                var (x, y) = CopyWindowPosition(windowDict);

                if (pid <= 0 || string.IsNullOrWhiteSpace(ownerName))
                {
                    continue;
                }

                windows.Add(new WindowInfo(
                    AppName: ownerName,
                    WindowTitle: windowName,
                    ProcessId: pid,
                    X: x,
                    Y: y));
            }

            var result = windows
                .DistinctBy(w => (w.ProcessId, w.WindowTitle, Math.Round(w.X, 2), Math.Round(w.Y, 2)))
                .OrderBy(w => w.AppName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(w => w.ProcessId)
                .ThenBy(w => w.WindowTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AppLogger.Info($"GetOpenWindows end. windows={result.Count}");
            return result;
        }
        finally
        {
            NativeMethods.CFRelease(windowsRef);
        }
    }

    public IReadOnlyList<AxElementInfo> GetAxElements(WindowInfo window)
    {
        AppLogger.Info($"GetAxElements begin. pid={window.ProcessId}, app={window.AppName}, TargetPos=({window.X}, {window.Y})");

        if (!OperatingSystem.IsMacOS())
        {
            AppLogger.Info("GetAxElements skipped: non-macOS.");
            return [];
        }

        if (!NativeMethods.AXIsProcessTrusted())
        {
            AppLogger.Error("GetAxElements blocked: AX permission not granted.");
            throw new InvalidOperationException("アクセシビリティ権限がありません。");
        }

        var appElement = NativeMethods.AXUIElementCreateApplication(window.ProcessId);
        if (appElement == IntPtr.Zero)
        {
            AppLogger.Info("GetAxElements end: app element was null.");
            return [];
        }

        try
        {
            NativeMethods.AXUIElementSetMessagingTimeout(appElement, AxMessageTimeoutSeconds);

            var targetWindowElement = FindTargetWindowElement(appElement, window);
            if (targetWindowElement == IntPtr.Zero)
            {
                AppLogger.Info("GetAxElements end: target window element not found.");
                return [];
            }

            var elements = EnumerateAxElements(targetWindowElement);
            AppLogger.Info($"GetAxElements end. elements={elements.Count}");
            return elements;
        }
        finally
        {
            NativeMethods.CFRelease(appElement);
        }
    }

    public void MoveMouse(AxElementInfo element)
    {
        if (!element.X.HasValue || !element.Y.HasValue || !element.Width.HasValue || !element.Height.HasValue) return;

        var centerX = element.X.Value + element.Width.Value / 2;
        var centerY = element.Y.Value + element.Height.Value / 2;
        var point = new NativeMethods.CgPoint { X = centerX, Y = centerY };

        AppLogger.Info($"MoveMouse to ({centerX}, {centerY}) | Element: {element.Title}, Role: {element.Role}");
        var moveEvent = NativeMethods.CGEventCreateMouseEvent(IntPtr.Zero, NativeMethods.KcgEventMouseMoved, point, 0);
        if (moveEvent != IntPtr.Zero)
        {
            NativeMethods.CGEventPost(NativeMethods.KcgHidEventTap, moveEvent);
            NativeMethods.CFRelease(moveEvent);
        }
    }

    public void LeftClick(AxElementInfo element)
    {
        if (!element.X.HasValue || !element.Y.HasValue || !element.Width.HasValue || !element.Height.HasValue)
        {
            AppLogger.Error("LeftClick failed: Element has no position/size.");
            return;
        }

        var centerX = element.X.Value + element.Width.Value / 2;
        var centerY = element.Y.Value + element.Height.Value / 2;
        var point = new NativeMethods.CgPoint { X = centerX, Y = centerY };

        AppLogger.Info($"LeftClick at ({centerX}, {centerY}) | Element: {element.Title}");
        PostClick(point, NativeMethods.KcgMouseButtonLeft, 1);
    }

    public void RightClick(AxElementInfo element)
    {
        if (!element.X.HasValue || !element.Y.HasValue || !element.Width.HasValue || !element.Height.HasValue)
        {
            AppLogger.Error("RightClick failed: Element has no position/size.");
            return;
        }

        var centerX = element.X.Value + element.Width.Value / 2;
        var centerY = element.Y.Value + element.Height.Value / 2;
        var point = new NativeMethods.CgPoint { X = centerX, Y = centerY };

        AppLogger.Info($"RightClick at ({centerX}, {centerY}) | Element: {element.Title}");
        PostClick(point, NativeMethods.KcgMouseButtonRight, 1);
    }

    public void DoubleLeftClick(AxElementInfo element)
    {
        if (!element.X.HasValue || !element.Y.HasValue || !element.Width.HasValue || !element.Height.HasValue)
        {
            AppLogger.Error("DoubleLeftClick failed: Element has no position/size.");
            return;
        }

        var centerX = element.X.Value + element.Width.Value / 2;
        var centerY = element.Y.Value + element.Height.Value / 2;
        var point = new NativeMethods.CgPoint { X = centerX, Y = centerY };

        AppLogger.Info($"DoubleLeftClick at ({centerX}, {centerY}) | Element: {element.Title}");
        PostClick(point, NativeMethods.KcgMouseButtonLeft, 2);
    }

    private static void PostClick(NativeMethods.CgPoint point, uint button, int clickCount)
    {
        uint downType, upType;
        if (button == NativeMethods.KcgMouseButtonLeft)
        {
            downType = NativeMethods.KcgEventLeftMouseDown;
            upType = NativeMethods.KcgEventLeftMouseUp;
        }
        else if (button == NativeMethods.KcgMouseButtonRight)
        {
            downType = NativeMethods.KcgEventRightMouseDown;
            upType = NativeMethods.KcgEventRightMouseUp;
        }
        else
        {
            return;
        }

        // 移動イベントも送る（念のため）
        var moveEvent = NativeMethods.CGEventCreateMouseEvent(IntPtr.Zero, NativeMethods.KcgEventMouseMoved, point, 0);
        if (moveEvent != IntPtr.Zero)
        {
            NativeMethods.CGEventPost(NativeMethods.KcgHidEventTap, moveEvent);
            NativeMethods.CFRelease(moveEvent);
        }

        for (int i = 1; i <= clickCount; i++)
        {
            var downEvent = NativeMethods.CGEventCreateMouseEvent(IntPtr.Zero, downType, point, button);
            if (downEvent != IntPtr.Zero)
            {
                NativeMethods.CGEventSetIntegerValueField(downEvent, NativeMethods.KcgMouseEventClickState, i);
                NativeMethods.CGEventPost(NativeMethods.KcgHidEventTap, downEvent);
                NativeMethods.CFRelease(downEvent);
            }

            var upEvent = NativeMethods.CGEventCreateMouseEvent(IntPtr.Zero, upType, point, button);
            if (upEvent != IntPtr.Zero)
            {
                NativeMethods.CGEventSetIntegerValueField(upEvent, NativeMethods.KcgMouseEventClickState, i);
                NativeMethods.CGEventPost(NativeMethods.KcgHidEventTap, upEvent);
                NativeMethods.CFRelease(upEvent);
            }

            if (i < clickCount) Thread.Sleep(10);
        }
    }

    private static IntPtr FindTargetWindowElement(IntPtr appElement, WindowInfo target)
    {
        var windows = CopyAttributeArray(appElement, "AXWindows");
        AppLogger.Info($"FindTargetWindowElement: Found {windows.Count} windows in app.");

        foreach (var windowElement in windows)
        {
            var title = CopyAttributeString(windowElement, "AXTitle");
            var (x, y) = CopyAttributePosition(windowElement, "AXPosition");
            
            // 影の影響を考慮して閾値を広げる (1px -> 50px)
            var diffX = Math.Abs(x - target.X);
            var diffY = Math.Abs(y - target.Y);

            AppLogger.Info($"Checking Window: Title='{title}', Pos=({x}, {y}), Diff=({diffX}, {diffY})");

            if (string.Equals(title, target.WindowTitle, StringComparison.Ordinal) && diffX < 50 && diffY < 50)
            {
                AppLogger.Info("Window matched by Title and Position.");
                // 他のハンドルを解放
                foreach (var item in windows) if (item != windowElement) NativeMethods.CFRelease(item);
                return windowElement;
            }
        }

        // フォールバック
        AppLogger.Info("No exact match. Falling back to FocusedWindow or first window.");
        var focused = CopyAttributeElement(appElement, "AXFocusedWindow");
        if (focused != IntPtr.Zero)
        {
            foreach (var item in windows) NativeMethods.CFRelease(item);
            return focused;
        }

        if (windows.Count > 0)
        {
            var first = windows[0];
            for (int i = 1; i < windows.Count; i++) NativeMethods.CFRelease(windows[i]);
            return first;
        }

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
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = CopyAttributeString(element, "AXDescription");
                }

                var actions = CopyActionNames(element);
                var (positionX, positionY) = CopyAttributePositionNullable(element, "AXPosition");
                var (width, height) = CopyAttributeSizeNullable(element, "AXSize");

                results.Add(new AxElementInfo(
                    Depth: depth,
                    Role: string.IsNullOrWhiteSpace(role) ? "(なし)" : role,
                    Subrole: string.IsNullOrWhiteSpace(subrole) ? "(なし)" : subrole,
                    Title: string.IsNullOrWhiteSpace(title) ? "(なし)" : title,
                    Actions: string.IsNullOrWhiteSpace(actions) ? "(なし)" : actions,
                    X: positionX,
                    Y: positionY,
                    Width: width,
                    Height: height));

                var children = CopyAttributeArray(element, "AXChildren");
                foreach (var child in children)
                {
                    queue.Enqueue((child, depth + 1));
                }
            }
            finally
            {
                NativeMethods.CFRelease(element);
            }
        }

        return results;
    }

    private static List<IntPtr> CopyAttributeArray(IntPtr element, string attribute)
    {
        var attributeRef = CreateCfString(attribute);
        try
        {
            var error = NativeMethods.AXUIElementCopyAttributeValue(element, attributeRef, out var valueRef);
            if (error != NativeMethods.AXError.Success || valueRef == IntPtr.Zero)
            {
                return [];
            }

            try
            {
                var count = NativeMethods.CFArrayGetCount(valueRef);
                var result = new List<IntPtr>((int)count);

                for (nint i = 0; i < count; i++)
                {
                    var item = NativeMethods.CFArrayGetValueAtIndex(valueRef, i);
                    if (item != IntPtr.Zero)
                    {
                        NativeMethods.CFRetain(item);
                        result.Add(item);
                    }
                }

                return result;
            }
            finally
            {
                NativeMethods.CFRelease(valueRef);
            }
        }
        finally
        {
            NativeMethods.CFRelease(attributeRef);
        }
    }

    private static IntPtr CopyAttributeElement(IntPtr element, string attribute)
    {
        var attributeRef = CreateCfString(attribute);
        try
        {
            var error = NativeMethods.AXUIElementCopyAttributeValue(element, attributeRef, out var valueRef);
            if (error != NativeMethods.AXError.Success || valueRef == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return valueRef;
        }
        finally
        {
            NativeMethods.CFRelease(attributeRef);
        }
    }

    private static string CopyAttributeString(IntPtr element, string attribute)
    {
        var attributeRef = CreateCfString(attribute);
        try
        {
            var error = NativeMethods.AXUIElementCopyAttributeValue(element, attributeRef, out var valueRef);
            if (error != NativeMethods.AXError.Success || valueRef == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                return CfStringToManaged(valueRef);
            }
            finally
            {
                NativeMethods.CFRelease(valueRef);
            }
        }
        finally
        {
            NativeMethods.CFRelease(attributeRef);
        }
    }

    private static (double X, double Y) CopyAttributePosition(IntPtr element, string attribute)
    {
        var attributeRef = CreateCfString(attribute);
        try
        {
            var error = NativeMethods.AXUIElementCopyAttributeValue(element, attributeRef, out var valueRef);
            if (error != NativeMethods.AXError.Success || valueRef == IntPtr.Zero)
            {
                return (0, 0);
            }

            try
            {
                var type = NativeMethods.AXValueGetType(valueRef);
                if (type != NativeMethods.AXValueType.CgPoint)
                {
                    return (0, 0);
                }

                var point = new NativeMethods.CgPoint();
                var ok = NativeMethods.AXValueGetValue(valueRef, NativeMethods.AXValueType.CgPoint, ref point);
                return ok ? (point.X, point.Y) : (0, 0);
            }
            finally
            {
                NativeMethods.CFRelease(valueRef);
            }
        }
        finally
        {
            NativeMethods.CFRelease(attributeRef);
        }
    }

    private static (double? X, double? Y) CopyAttributePositionNullable(IntPtr element, string attribute)
    {
        var attributeRef = CreateCfString(attribute);
        try
        {
            var error = NativeMethods.AXUIElementCopyAttributeValue(element, attributeRef, out var valueRef);
            if (error != NativeMethods.AXError.Success || valueRef == IntPtr.Zero)
            {
                return (null, null);
            }

            try
            {
                var type = NativeMethods.AXValueGetType(valueRef);
                if (type != NativeMethods.AXValueType.CgPoint)
                {
                    return (null, null);
                }

                var point = new NativeMethods.CgPoint();
                var ok = NativeMethods.AXValueGetValue(valueRef, NativeMethods.AXValueType.CgPoint, ref point);
                return ok ? (point.X, point.Y) : (null, null);
            }
            finally
            {
                NativeMethods.CFRelease(valueRef);
            }
        }
        finally
        {
            NativeMethods.CFRelease(attributeRef);
        }
    }

    private static (double? Width, double? Height) CopyAttributeSizeNullable(IntPtr element, string attribute)
    {
        var attributeRef = CreateCfString(attribute);
        try
        {
            var error = NativeMethods.AXUIElementCopyAttributeValue(element, attributeRef, out var valueRef);
            if (error != NativeMethods.AXError.Success || valueRef == IntPtr.Zero)
            {
                return (null, null);
            }

            try
            {
                var type = NativeMethods.AXValueGetType(valueRef);
                if (type != NativeMethods.AXValueType.CgSize)
                {
                    return (null, null);
                }

                var size = new NativeMethods.CgSize();
                var ok = NativeMethods.AXValueGetValue(valueRef, NativeMethods.AXValueType.CgSize, ref size);
                return ok ? (size.Width, size.Height) : (null, null);
            }
            finally
            {
                NativeMethods.CFRelease(valueRef);
            }
        }
        finally
        {
            NativeMethods.CFRelease(attributeRef);
        }
    }

    private static string CopyActionNames(IntPtr element)
    {
        var error = NativeMethods.AXUIElementCopyActionNames(element, out var actionsRef);
        if (error != NativeMethods.AXError.Success || actionsRef == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var count = NativeMethods.CFArrayGetCount(actionsRef);
            if (count <= 0)
            {
                return string.Empty;
            }

            var list = new List<string>((int)count);
            for (nint i = 0; i < count; i++)
            {
                var actionRef = NativeMethods.CFArrayGetValueAtIndex(actionsRef, i);
                if (actionRef == IntPtr.Zero)
                {
                    continue;
                }

                var action = CfStringToManaged(actionRef);
                if (!string.IsNullOrWhiteSpace(action))
                {
                    list.Add(action);
                }
            }

            return string.Join(", ", list);
        }
        finally
        {
            NativeMethods.CFRelease(actionsRef);
        }
    }

    private static string CopyDictionaryString(IntPtr dictionary, string key)
    {
        var keyRef = CreateCfString(key);
        try
        {
            var valueRef = NativeMethods.CFDictionaryGetValue(dictionary, keyRef);
            if (valueRef == IntPtr.Zero)
            {
                return string.Empty;
            }

            var typeId = NativeMethods.CFGetTypeID(valueRef);
            if (typeId != NativeMethods.CFStringGetTypeID())
            {
                return string.Empty;
            }

            return CfStringToManaged(valueRef);
        }
        finally
        {
            NativeMethods.CFRelease(keyRef);
        }
    }

    private static int CopyDictionaryInt(IntPtr dictionary, string key)
    {
        var keyRef = CreateCfString(key);
        try
        {
            var valueRef = NativeMethods.CFDictionaryGetValue(dictionary, keyRef);
            if (valueRef == IntPtr.Zero)
            {
                return 0;
            }

            var typeId = NativeMethods.CFGetTypeID(valueRef);
            if (typeId != NativeMethods.CFNumberGetTypeID())
            {
                return 0;
            }

            long longValue = 0;
            var ok = NativeMethods.CFNumberGetValue(valueRef, NativeMethods.CFNumberType.SInt64, out longValue);
            return ok ? (int)longValue : 0;
        }
        finally
        {
            NativeMethods.CFRelease(keyRef);
        }
    }

    private static double CopyDictionaryDouble(IntPtr dictionary, string key)
    {
        var keyRef = CreateCfString(key);
        try
        {
            var valueRef = NativeMethods.CFDictionaryGetValue(dictionary, keyRef);
            if (valueRef == IntPtr.Zero)
            {
                return 0;
            }

            var typeId = NativeMethods.CFGetTypeID(valueRef);
            if (typeId != NativeMethods.CFNumberGetTypeID())
            {
                return 0;
            }

            double doubleValue = 0;
            var ok = NativeMethods.CFNumberGetValue(valueRef, NativeMethods.CFNumberType.Float64, out doubleValue);
            return ok ? doubleValue : 0;
        }
        finally
        {
            NativeMethods.CFRelease(keyRef);
        }
    }

    private static (double X, double Y) CopyWindowPosition(IntPtr windowDictionary)
    {
        var boundsKeyRef = CreateCfString("kCGWindowBounds");
        try
        {
            var boundsRef = NativeMethods.CFDictionaryGetValue(windowDictionary, boundsKeyRef);
            if (boundsRef == IntPtr.Zero)
            {
                return (0, 0);
            }

            var typeId = NativeMethods.CFGetTypeID(boundsRef);
            if (typeId != NativeMethods.CFDictionaryGetTypeID())
            {
                return (0, 0);
            }

            var x = CopyDictionaryDouble(boundsRef, "X");
            var y = CopyDictionaryDouble(boundsRef, "Y");
            return (x, y);
        }
        finally
        {
            NativeMethods.CFRelease(boundsKeyRef);
        }
    }

    private static IntPtr CreateCfString(string value)
    {
        var cfString = NativeMethods.CFStringCreateWithCString(IntPtr.Zero, value, KcfStringEncodingUtf8);
        if (cfString == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CFString の作成に失敗しました: {value}");
        }

        return cfString;
    }

    private static string CfStringToManaged(IntPtr cfString)
    {
        if (cfString == IntPtr.Zero)
        {
            return string.Empty;
        }

        var length = NativeMethods.CFStringGetLength(cfString);
        if (length <= 0)
        {
            return string.Empty;
        }

        var maxSize = NativeMethods.CFStringGetMaximumSizeForEncoding(length, KcfStringEncodingUtf8) + 1;
        var buffer = Marshal.AllocHGlobal((int)maxSize);

        try
        {
            var success = NativeMethods.CFStringGetCString(cfString, buffer, maxSize, KcfStringEncodingUtf8);
            return success ? Marshal.PtrToStringUTF8(buffer) ?? string.Empty : string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static class NativeMethods
    {
        private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

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

        internal enum AXError
        {
            Success = 0,
            Failure = -25200,
            IllegalArgument = -25201,
            InvalidUIElement = -25202,
            InvalidUIElementObserver = -25203,
            CannotComplete = -25204,
            AttributeUnsupported = -25205,
            ActionUnsupported = -25206,
            NotificationUnsupported = -25207,
            NotImplemented = -25208,
            NotificationAlreadyRegistered = -25209,
            NotificationNotRegistered = -25210,
            ApiDisabled = -25211,
            NoValue = -25212,
            ParameterizedAttributeUnsupported = -25213,
            NotEnoughPrecision = -25214
        }

        internal enum AXValueType
        {
            CgPoint = 1,
            CgSize = 2,
            CgRect = 3,
            CfRange = 4,
            AxError = 5,
            Illegal = 0
        }

        internal enum CFNumberType
        {
            SInt64 = 4,
            Float64 = 13
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CgPoint
        {
            public double X;
            public double Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CgSize
        {
            public double Width;
            public double Height;
        }

        [DllImport(ApplicationServices)]
        internal static extern bool AXIsProcessTrusted();

        [DllImport(ApplicationServices)]
        internal static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

        [DllImport(ApplicationServices)]
        internal static extern IntPtr AXUIElementCreateApplication(int pid);

        [DllImport(ApplicationServices)]
        internal static extern AXError AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

        [DllImport(ApplicationServices)]
        internal static extern AXError AXUIElementCopyActionNames(IntPtr element, out IntPtr actions);

        [DllImport(ApplicationServices)]
        internal static extern AXError AXUIElementSetMessagingTimeout(IntPtr element, double timeoutInSeconds);

        [DllImport(ApplicationServices)]
        internal static extern AXValueType AXValueGetType(IntPtr value);

        [DllImport(ApplicationServices)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool AXValueGetValue(IntPtr value, AXValueType type, ref CgPoint point);

        [DllImport(ApplicationServices)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool AXValueGetValue(IntPtr value, AXValueType type, ref CgSize size);

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string str, uint encoding);

        [DllImport(CoreFoundation)]
        internal static extern nint CFStringGetLength(IntPtr theString);

        [DllImport(CoreFoundation)]
        internal static extern nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);

        [DllImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool CFStringGetCString(IntPtr theString, IntPtr buffer, nint bufferSize, uint encoding);

        [DllImport(CoreFoundation)]
        internal static extern nint CFArrayGetCount(IntPtr theArray);

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFArrayGetValueAtIndex(IntPtr theArray, nint idx);

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFDictionaryGetValue(IntPtr theDict, IntPtr key);

        [DllImport(CoreFoundation)]
        internal static extern ulong CFGetTypeID(IntPtr cf);

        [DllImport(CoreFoundation)]
        internal static extern ulong CFStringGetTypeID();

        [DllImport(CoreFoundation)]
        internal static extern ulong CFNumberGetTypeID();

        [DllImport(CoreFoundation)]
        internal static extern ulong CFDictionaryGetTypeID();

        [DllImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool CFNumberGetValue(IntPtr number, CFNumberType theType, out long value);

        [DllImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool CFNumberGetValue(IntPtr number, CFNumberType theType, out double value);

        [DllImport(CoreFoundation)]
        internal static extern void CFRelease(IntPtr cf);

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFRetain(IntPtr cf);

        [DllImport(CoreGraphics)]
        internal static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint mouseType, CgPoint mouseCursorPosition, uint mouseButton);

        [DllImport(CoreGraphics)]
        internal static extern void CGEventPost(uint tap, IntPtr eventRef);

        [DllImport(CoreGraphics)]
        internal static extern void CGEventSetIntegerValueField(IntPtr eventRef, int field, long value);
    }
}
