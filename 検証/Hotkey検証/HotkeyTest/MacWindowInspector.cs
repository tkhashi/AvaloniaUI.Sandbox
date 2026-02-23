using System.Runtime.InteropServices;
using System.Text;

namespace HotkeyTest;

internal sealed class MacWindowInspector
{
    public WindowContext? TryGetFrontWindowContext()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        var array = CGWindowListCopyWindowInfo(CGWindowListOptionOnScreenOnly | CGWindowListExcludeDesktopElements, 0);
        if (array == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var count = CFArrayGetCount(array);
            for (nint i = 0; i < count; i++)
            {
                var dict = CFArrayGetValueAtIndex(array, i);
                if (dict == IntPtr.Zero)
                {
                    continue;
                }

                var layer = GetIntFromDictionary(dict, "kCGWindowLayer");
                if (layer != 0)
                {
                    continue;
                }

                var width = GetDoubleFromBoundsDictionary(dict, "Width");
                var height = GetDoubleFromBoundsDictionary(dict, "Height");
                if (width < 80 || height < 80)
                {
                    continue;
                }

                var processName = GetStringFromDictionary(dict, "kCGWindowOwnerName");
                var pid = (int)GetIntFromDictionary(dict, "kCGWindowOwnerPID");
                var windowId = (uint)GetIntFromDictionary(dict, "kCGWindowNumber");
                var x = GetDoubleFromBoundsDictionary(dict, "X");
                var y = GetDoubleFromBoundsDictionary(dict, "Y");

                if (string.IsNullOrWhiteSpace(processName) || pid <= 0 || windowId == 0)
                {
                    continue;
                }

                return new WindowContext(processName, pid, windowId, x, y, width, height);
            }

            return null;
        }
        finally
        {
            CFRelease(array);
        }
    }

    public double TryAnalyzeWindowLuminance(WindowContext context)
    {
        var bounds = new CGRect
        {
            Origin = new CGPoint { X = context.X, Y = context.Y },
            Size = new CGSize { Width = context.Width, Height = context.Height }
        };

        var image = CGWindowListCreateImage(bounds, CGWindowListOptionIncludingWindow, context.WindowId, CGWindowImageNominalResolution);
        if (image == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            var width = CGImageGetWidth(image);
            var height = CGImageGetHeight(image);
            var bytesPerRow = CGImageGetBytesPerRow(image);
            if (width <= 0 || height <= 0 || bytesPerRow <= 0)
            {
                return 0;
            }

            var provider = CGImageGetDataProvider(image);
            if (provider == IntPtr.Zero)
            {
                return 0;
            }

            var data = CGDataProviderCopyData(provider);
            if (data == IntPtr.Zero)
            {
                return 0;
            }

            try
            {
                var length = (int)CFDataGetLength(data);
                var bytes = CFDataGetBytePtr(data);
                if (bytes == IntPtr.Zero || length <= 0)
                {
                    return 0;
                }

                var managed = new byte[length];
                Marshal.Copy(bytes, managed, 0, length);

                var sampleStep = 8;
                double luminanceTotal = 0;
                var count = 0;

                for (var y = 0; y < height; y += sampleStep)
                {
                    var rowStart = y * bytesPerRow;
                    for (var x = 0; x < width; x += sampleStep)
                    {
                        var index = rowStart + (x * 4);
                        if (index + 2 >= managed.Length)
                        {
                            continue;
                        }

                        var b = managed[index] / 255.0;
                        var g = managed[index + 1] / 255.0;
                        var r = managed[index + 2] / 255.0;

                        var luma = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
                        luminanceTotal += luma;
                        count++;
                    }
                }

                return count == 0 ? 0 : luminanceTotal / count;
            }
            finally
            {
                CFRelease(data);
            }
        }
        finally
        {
            CGImageRelease(image);
        }
    }

    private static string GetStringFromDictionary(IntPtr dictionary, string key)
    {
        var value = GetDictionaryValue(dictionary, key);
        if (value == IntPtr.Zero)
        {
            return string.Empty;
        }

        return CFStringToString(value);
    }

    private static long GetIntFromDictionary(IntPtr dictionary, string key)
    {
        var value = GetDictionaryValue(dictionary, key);
        if (value == IntPtr.Zero)
        {
            return 0;
        }

        return CFNumberGetValue(value, 4, out long n) ? n : 0;
    }

    private static double GetDoubleFromBoundsDictionary(IntPtr windowDictionary, string key)
    {
        var bounds = GetDictionaryValue(windowDictionary, "kCGWindowBounds");
        if (bounds == IntPtr.Zero)
        {
            return 0;
        }

        var value = GetDictionaryValue(bounds, key);
        if (value == IntPtr.Zero)
        {
            return 0;
        }

        return CFNumberGetValue(value, 13, out double n) ? n : 0;
    }

    private static IntPtr GetDictionaryValue(IntPtr dictionary, string key)
    {
        var keyRef = GetKeyStringRef(key);
        return CFDictionaryGetValue(dictionary, keyRef);
    }

    private static string CFStringToString(IntPtr cfString)
    {
        var length = CFStringGetLength(cfString);
        if (length <= 0)
        {
            return string.Empty;
        }

        var maxBytes = CFStringGetMaximumSizeForEncoding(length, Utf8Encoding) + 1;
        var buffer = new byte[maxBytes];

        if (!CFStringGetCString(cfString, buffer, buffer.Length, Utf8Encoding))
        {
            return string.Empty;
        }

        var nullIndex = Array.IndexOf(buffer, (byte)0);
        if (nullIndex < 0)
        {
            nullIndex = buffer.Length;
        }

        return Encoding.UTF8.GetString(buffer, 0, nullIndex);
    }

    private static IntPtr GetKeyStringRef(string key)
    {
        if (KeyRefs.TryGetValue(key, out var value))
        {
            return value;
        }

        value = CFStringCreateWithCString(IntPtr.Zero, key, Utf8Encoding);
        KeyRefs[key] = value;
        return value;
    }

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int Utf8Encoding = 0x08000100;

    private const uint CGWindowListOptionOnScreenOnly = 1;
    private const uint CGWindowListExcludeDesktopElements = 16;
    private const uint CGWindowListOptionIncludingWindow = 8;
    private const uint CGWindowImageNominalResolution = 8;

    private static readonly Dictionary<string, IntPtr> KeyRefs = new(StringComparer.Ordinal)
    {
        ["kCGWindowLayer"] = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowLayer", Utf8Encoding),
        ["kCGWindowOwnerName"] = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerName", Utf8Encoding),
        ["kCGWindowOwnerPID"] = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerPID", Utf8Encoding),
        ["kCGWindowNumber"] = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowNumber", Utf8Encoding),
        ["kCGWindowBounds"] = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowBounds", Utf8Encoding),
        ["X"] = CFStringCreateWithCString(IntPtr.Zero, "X", Utf8Encoding),
        ["Y"] = CFStringCreateWithCString(IntPtr.Zero, "Y", Utf8Encoding),
        ["Width"] = CFStringCreateWithCString(IntPtr.Zero, "Width", Utf8Encoding),
        ["Height"] = CFStringCreateWithCString(IntPtr.Zero, "Height", Utf8Encoding)
    };

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGWindowListCreateImage(CGRect screenBounds, uint listOption, uint windowID, uint imageOption);

    [DllImport(CoreGraphics)]
    private static extern int CGImageGetWidth(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern int CGImageGetHeight(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern int CGImageGetBytesPerRow(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGImageGetDataProvider(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern void CGImageRelease(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGDataProviderCopyData(IntPtr provider);

    [DllImport(CoreFoundation)]
    private static extern nint CFArrayGetCount(IntPtr array);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

    [DllImport(CoreFoundation)]
    private static extern bool CFNumberGetValue(IntPtr number, int type, out long value);

    [DllImport(CoreFoundation)]
    private static extern bool CFNumberGetValue(IntPtr number, int type, out double value);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern nint CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetLength(IntPtr theString);

    [DllImport(CoreFoundation)]
    private static extern int CFStringGetMaximumSizeForEncoding(nint length, int encoding);

    [DllImport(CoreFoundation)]
    private static extern bool CFStringGetCString(IntPtr handle, byte[] buffer, nint bufferSize, int encoding);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string str, int encoding);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGSize
    {
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public CGPoint Origin;
        public CGSize Size;
    }
}

internal readonly record struct WindowContext(string ProcessName, int ProcessId, uint WindowId, double X, double Y, double Width, double Height);