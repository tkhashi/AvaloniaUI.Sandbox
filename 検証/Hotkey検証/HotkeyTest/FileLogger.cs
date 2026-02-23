namespace HotkeyTest;

internal sealed class FileLogger
{
    private static readonly object Gate = new();
    private readonly string _logPath;

    public FileLogger()
    {
        var baseDir = ResolveBaseDirectory();
        Directory.CreateDirectory(baseDir);
        _logPath = Path.Combine(baseDir, "hotkey-test.log");
    }

    public void Info(string message)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}";
        lock (Gate)
        {
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
    }

    private static string ResolveBaseDirectory()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates)
        {
            var current = new DirectoryInfo(candidate);
            while (current is not null)
            {
                var requirementFile = Path.Combine(current.FullName, "検証要件.md");
                if (File.Exists(requirementFile))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return Directory.GetCurrentDirectory();
    }
}