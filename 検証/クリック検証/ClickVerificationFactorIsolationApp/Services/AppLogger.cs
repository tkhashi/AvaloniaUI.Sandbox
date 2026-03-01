using System;
using System.IO;
using System.Text;

namespace ClickVerificationFactorIsolationApp.Services;

public static class AppLogger
{
    private static readonly object Gate = new();
    private static string? _logFilePath;

    public static string LogFilePath => _logFilePath ?? string.Empty;

    public static void Initialize()
    {
        lock (Gate)
        {
            if (!string.IsNullOrWhiteSpace(_logFilePath))
            {
                return;
            }

            var directory = ResolveLogDirectory();
            Directory.CreateDirectory(directory);

            _logFilePath = Path.Combine(directory, "accessibility-api-factor-isolation.log");
            WriteCore("INFO", $"Logger initialized. path={_logFilePath}");
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(Exception ex, string message)
    {
        Write("ERROR", $"{message} | {ex.GetType().Name}: {ex.Message}");
        Write("ERROR", ex.StackTrace ?? "(stacktrace none)");
    }

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(_logFilePath))
            {
                Initialize();
            }

            WriteCore(level, message);
        }
    }

    private static void WriteCore(string level, string message)
    {
        if (string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}";
        File.AppendAllText(_logFilePath, line, Encoding.UTF8);
    }

    private static string ResolveLogDirectory()
    {
        const string markerFileName = "検証要件.md";

        var currentDirectory = Directory.GetCurrentDirectory();
        var byCurrent = FindDirectoryContainingMarker(currentDirectory, markerFileName);
        if (byCurrent is not null)
        {
            return byCurrent;
        }

        var byBase = FindDirectoryContainingMarker(AppContext.BaseDirectory, markerFileName);
        if (byBase is not null)
        {
            return byBase;
        }

        return currentDirectory;
    }

    private static string? FindDirectoryContainingMarker(string startDirectory, string markerFileName)
    {
        try
        {
            var dir = new DirectoryInfo(startDirectory);
            for (var depth = 0; depth < 12 && dir is not null; depth++)
            {
                var markerPath = Path.Combine(dir.FullName, markerFileName);
                if (File.Exists(markerPath))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
