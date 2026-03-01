using System;
using System.IO;

namespace ToolbarIconUiVerificationApp.Services;

internal static class AppLogger
{
    private static readonly object Sync = new();
    public static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "toolbar-ui-verification.log");

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Error(string message, Exception ex)
    {
        Write("ERROR", $"{message}: {ex.Message}");
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}";
        lock (Sync)
        {
            File.AppendAllText(LogPath, line);
        }
    }
}
