using Avalonia;
using System;
using System.Globalization;
using System.Linq;
using ClickVerificationDelayedLoadApp.Services;

namespace ClickVerificationDelayedLoadApp;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (TryBuildCliOptions(args, out var options))
        {
            RunDelayLoadCli(options);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static bool TryBuildCliOptions(string[] args, out DelayLoadCliOptions options)
    {
        options = default;

        if (!args.Any(arg => string.Equals(arg, "--delay-load-check", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var appName = GetOptionValue(args, "--app-name") ?? "Vivaldi";
        var titleContains = GetOptionValue(args, "--title-contains");
        var attempts = ParseIntOption(args, "--attempts", 6, min: 1);
        var intervalMs = ParseIntOption(args, "--interval-ms", 1200, min: 0);
        var timeoutMs = ParseIntOption(args, "--timeout-ms", 8000, min: 1000);

        options = new DelayLoadCliOptions(appName, titleContains, attempts, intervalMs, timeoutMs);
        return true;
    }

    private static void RunDelayLoadCli(DelayLoadCliOptions options)
    {
        AppLogger.Initialize();
        AppLogger.Info(
            $"CLI delay-load-check start. app={options.AppName}, titleContains={options.TitleContains ?? "(none)"}, attempts={options.Attempts}, intervalMs={options.IntervalMs}, timeoutMs={options.TimeoutMs}");

        var service = new MacAccessibilityService();
        var windows = service.GetOpenWindows();

        var target = windows
            .Where(window => window.AppName.Contains(options.AppName, StringComparison.OrdinalIgnoreCase))
            .Where(window => string.IsNullOrWhiteSpace(options.TitleContains)
                || window.WindowTitle.Contains(options.TitleContains, StringComparison.OrdinalIgnoreCase))
            .OrderBy(window => window.AppName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (target is null)
        {
            Console.WriteLine($"対象ウィンドウが見つかりませんでした。 app={options.AppName}, titleContains={options.TitleContains ?? "(none)"}");
            AppLogger.Error("CLI delay-load-check target window not found.");
            Environment.ExitCode = 2;
            return;
        }

        Console.WriteLine($"対象: {target}");
        var result = service.VerifyDelayedLoadAsync(
                target,
                options.Attempts,
                TimeSpan.FromMilliseconds(options.IntervalMs),
                TimeSpan.FromMilliseconds(options.TimeoutMs))
            .GetAwaiter()
            .GetResult();

        foreach (var attempt in result.Attempts)
        {
            Console.WriteLine(attempt.ToString());
        }

        Console.WriteLine(
            $"判定: {(result.IsLikelyDelayedLoad ? "遅延ロードの可能性あり" : "遅延ロードが主要因ではない可能性が高い")} | 理由: {result.VerdictReason}");

        AppLogger.Info(
            $"CLI delay-load-check end. delayedLikely={result.IsLikelyDelayedLoad}, reason={result.VerdictReason}, first={result.FirstElementCount}, last={result.LastElementCount}, max={result.MaxElementCount}");
    }

    private static string? GetOptionValue(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static int ParseIntOption(string[] args, string optionName, int defaultValue, int min)
    {
        var valueText = GetOptionValue(args, optionName);
        if (string.IsNullOrWhiteSpace(valueText))
        {
            return defaultValue;
        }

        if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return defaultValue;
        }

        return parsed < min ? defaultValue : parsed;
    }

    private readonly record struct DelayLoadCliOptions(
        string AppName,
        string? TitleContains,
        int Attempts,
        int IntervalMs,
        int TimeoutMs);
}
