using Avalonia;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ClickVerificationFactorIsolationApp.Models;
using ClickVerificationFactorIsolationApp.Services;

namespace ClickVerificationFactorIsolationApp;

class Program
{
    private static readonly HashSet<string> BrowserLikeRoles = new(StringComparer.Ordinal)
    {
        "AXStaticText", "AXLink", "AXTextField", "AXTextArea", "AXHeading",
        "AXImage", "AXList", "AXListItem", "AXRow", "AXCell", "AXButton"
    };

    [STAThread]
    public static void Main(string[] args)
    {
        if (TryBuildFactorCheckOptions(args, out var options))
        {
            RunFactorCheckAsync(options).GetAwaiter().GetResult();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static bool TryBuildFactorCheckOptions(string[] args, out FactorCheckOptions options)
    {
        options = default;
        if (!args.Any(arg => string.Equals(arg, "--factor-check", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var appName = GetOptionValue(args, "--app-name") ?? "Vivaldi";
        var titleContains = GetOptionValue(args, "--title-contains");
        var attempts = ParseIntOption(args, "--attempts", 5, min: 2);
        var intervalMs = ParseIntOption(args, "--interval-ms", 1000, min: 0);
        var preDelayMs = ParseIntOption(args, "--pre-delay-ms", 2500, min: 0);
        var maxCandidates = ParseIntOption(args, "--max-candidates", 5, min: 1);

        options = new FactorCheckOptions(appName, titleContains, attempts, intervalMs, preDelayMs, maxCandidates);
        return true;
    }

    private static async Task RunFactorCheckAsync(FactorCheckOptions options)
    {
        AppLogger.Initialize();
        AppLogger.Info(
            $"FactorCheck start. app={options.AppName}, titleContains={options.TitleContains ?? "(none)"}, attempts={options.Attempts}, intervalMs={options.IntervalMs}, preDelayMs={options.PreDelayMs}, maxCandidates={options.MaxCandidates}");

        var service = new MacAccessibilityService();
        var candidateWindows = service.GetOpenWindows()
            .Where(window => window.AppName.Contains(options.AppName, StringComparison.OrdinalIgnoreCase))
            .Where(window => string.IsNullOrWhiteSpace(options.TitleContains)
                || window.WindowTitle.Contains(options.TitleContains, StringComparison.OrdinalIgnoreCase))
            .Take(options.MaxCandidates)
            .ToList();

        if (candidateWindows.Count == 0)
        {
            Console.WriteLine($"対象ウィンドウが見つかりませんでした: app={options.AppName}, titleContains={options.TitleContains ?? "(none)"}");
            AppLogger.Error("FactorCheck target windows not found.");
            Environment.ExitCode = 2;
            return;
        }

        Console.WriteLine("=== 候補ウィンドウ ===");
        for (var i = 0; i < candidateWindows.Count; i++)
        {
            Console.WriteLine($"[{i + 1}] {candidateWindows[i]}");
        }

        var perWindowResults = new List<WindowFactorResult>(candidateWindows.Count);
        foreach (var candidate in candidateWindows)
        {
            var baselineSingle = await RunScenarioAsync(
                service,
                candidate,
                "BaselineSingle",
                WindowSelectionMode.MatchTitleAndPositionWithFallback,
                attempts: 1,
                intervalMs: 0,
                preDelayMs: 0);

            var baselineDelayed = await RunScenarioAsync(
                service,
                candidate,
                "BaselinePreDelaySingle",
                WindowSelectionMode.MatchTitleAndPositionWithFallback,
                attempts: 1,
                intervalMs: 0,
                preDelayMs: options.PreDelayMs);

            var baselineRetry = await RunScenarioAsync(
                service,
                candidate,
                "BaselineRetry",
                WindowSelectionMode.MatchTitleAndPositionWithFallback,
                attempts: options.Attempts,
                intervalMs: options.IntervalMs,
                preDelayMs: 0);

            var focusedSingle = await RunScenarioAsync(
                service,
                candidate,
                "FocusedSingle",
                WindowSelectionMode.FocusedWindowOnly,
                attempts: 1,
                intervalMs: 0,
                preDelayMs: 0);

            var focusedRetry = await RunScenarioAsync(
                service,
                candidate,
                "FocusedRetry",
                WindowSelectionMode.FocusedWindowOnly,
                attempts: options.Attempts,
                intervalMs: options.IntervalMs,
                preDelayMs: 0);

            var result = AnalyzeFactor(candidate, baselineSingle, baselineDelayed, baselineRetry, focusedSingle, focusedRetry);
            perWindowResults.Add(result);

            Console.WriteLine();
            Console.WriteLine($"--- {candidate} ---");
            PrintScenario(result.BaselineSingle);
            PrintScenario(result.BaselinePreDelaySingle);
            PrintScenario(result.BaselineRetry);
            PrintScenario(result.FocusedSingle);
            PrintScenario(result.FocusedRetry);
            Console.WriteLine($"判定: {result.PrimaryFactor} | 理由: {result.Reason}");
        }

        Console.WriteLine();
        Console.WriteLine("=== 総合判定 ===");
        var overall = BuildOverallVerdict(perWindowResults);
        Console.WriteLine(overall);
        AppLogger.Info($"FactorCheck end. overall={overall}");
    }

    private static async Task<ScenarioResult> RunScenarioAsync(
        MacAccessibilityService service,
        WindowInfo targetWindow,
        string name,
        WindowSelectionMode selectionMode,
        int attempts,
        int intervalMs,
        int preDelayMs)
    {
        if (preDelayMs > 0)
        {
            await Task.Delay(preDelayMs);
        }

        var results = new List<CaptureMetrics>(attempts);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                var elements = service.GetAxElements(targetWindow, selectionMode);
                var metrics = SummarizeElements(elements);
                results.Add(metrics);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, $"RunScenario failed. name={name}, mode={selectionMode}, attempt={attempt}");
                results.Add(CaptureMetrics.Empty(ex.Message));
            }

            if (attempt < attempts && intervalMs > 0)
            {
                await Task.Delay(intervalMs);
            }
        }

        return ScenarioResult.Create(name, selectionMode, results);
    }

    private static CaptureMetrics SummarizeElements(IReadOnlyList<AxElementInfo> elements)
    {
        var webAreas = elements.Count(element => string.Equals(element.Role, "AXWebArea", StringComparison.Ordinal));
        var browserLike = elements.Count(element => BrowserLikeRoles.Contains(element.Role));
        var titled = elements.Count(element => !string.IsNullOrWhiteSpace(element.Title));
        var maxDepth = elements.Count == 0 ? 0 : elements.Max(element => element.Depth);
        var uniqueSignature = elements
            .Select(element =>
            {
                var title = string.IsNullOrWhiteSpace(element.Title) ? "(none)" : element.Title.Trim();
                return $"{element.Role}|{element.Subrole}|{title}";
            })
            .Distinct(StringComparer.Ordinal)
            .Count();

        return new CaptureMetrics(
            elements.Count,
            webAreas,
            browserLike,
            titled,
            uniqueSignature,
            maxDepth,
            "");
    }

    private static WindowFactorResult AnalyzeFactor(
        WindowInfo window,
        ScenarioResult baselineSingle,
        ScenarioResult baselineDelayed,
        ScenarioResult baselineRetry,
        ScenarioResult focusedSingle,
        ScenarioResult focusedRetry)
    {
        var baselineTotal = baselineSingle.Best.TotalElements;
        var baselineScore = baselineSingle.Best.Score;
        var timingBest = Math.Max(baselineDelayed.Best.Score, baselineRetry.Best.Score);
        var focusBest = Math.Max(focusedSingle.Best.Score, focusedRetry.Best.Score);

        var timingGain = timingBest - baselineScore;
        var focusGain = focusBest - baselineScore;
        var threshold = Math.Max(80, baselineTotal / 3);

        string factor;
        string reason;
        if (focusGain >= threshold && focusGain > timingGain + 40)
        {
            factor = "フォーカス/対象ウィンドウ選択差";
            reason = $"Focused系で大幅増加 (gain={focusGain})。同一ロジックでも対象窓の差が主要因。";
        }
        else if (timingGain >= threshold)
        {
            factor = "取得タイミング/再試行差";
            reason = $"遅延または再試行で増加 (gain={timingGain})。初回単発では取得不足。";
        }
        else
        {
            factor = "有意差なし";
            reason = $"各シナリオ差が小さい (timingGain={timingGain}, focusGain={focusGain})。";
        }

        return new WindowFactorResult(
            window,
            baselineSingle,
            baselineDelayed,
            baselineRetry,
            focusedSingle,
            focusedRetry,
            factor,
            reason);
    }

    private static string BuildOverallVerdict(IReadOnlyList<WindowFactorResult> results)
    {
        var focusCount = results.Count(result => result.PrimaryFactor == "フォーカス/対象ウィンドウ選択差");
        var timingCount = results.Count(result => result.PrimaryFactor == "取得タイミング/再試行差");
        var neutralCount = results.Count - focusCount - timingCount;

        var baseTotals = results.Select(result => result.BaselineSingle.Best.TotalElements).ToList();
        var minBase = baseTotals.Min();
        var maxBase = baseTotals.Max();

        return $"focus={focusCount}, timing={timingCount}, neutral={neutralCount}, baselineRange={minBase}..{maxBase}";
    }

    private static void PrintScenario(ScenarioResult result)
    {
        Console.WriteLine(
            $"{result.Name,-22} mode={result.Mode,-34} first={result.First.TotalElements,5} best={result.Best.TotalElements,5} last={result.Last.TotalElements,5} web={result.Best.WebAreaCount,2} browserLike={result.Best.BrowserLikeCount,4} unique={result.Best.UniqueSignatureCount,4}");
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
        var value = GetOptionValue(args, optionName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return defaultValue;
        }

        return parsed < min ? defaultValue : parsed;
    }

    private readonly record struct FactorCheckOptions(
        string AppName,
        string? TitleContains,
        int Attempts,
        int IntervalMs,
        int PreDelayMs,
        int MaxCandidates);

    private readonly record struct CaptureMetrics(
        int TotalElements,
        int WebAreaCount,
        int BrowserLikeCount,
        int NonEmptyTitleCount,
        int UniqueSignatureCount,
        int MaxDepth,
        string ErrorMessage)
    {
        public int Score => TotalElements + (BrowserLikeCount * 4) + (UniqueSignatureCount * 2);

        public static CaptureMetrics Empty(string error)
            => new(0, 0, 0, 0, 0, 0, error);
    }

    private readonly record struct ScenarioResult(
        string Name,
        WindowSelectionMode Mode,
        CaptureMetrics First,
        CaptureMetrics Best,
        CaptureMetrics Last,
        IReadOnlyList<CaptureMetrics> Attempts)
    {
        public static ScenarioResult Create(string name, WindowSelectionMode mode, IReadOnlyList<CaptureMetrics> attempts)
        {
            if (attempts.Count == 0)
            {
                var empty = CaptureMetrics.Empty("NoAttempt");
                return new ScenarioResult(name, mode, empty, empty, empty, attempts);
            }

            var first = attempts[0];
            var last = attempts[^1];
            var best = attempts.OrderByDescending(attempt => attempt.Score).First();
            return new ScenarioResult(name, mode, first, best, last, attempts);
        }
    }

    private readonly record struct WindowFactorResult(
        WindowInfo Window,
        ScenarioResult BaselineSingle,
        ScenarioResult BaselinePreDelaySingle,
        ScenarioResult BaselineRetry,
        ScenarioResult FocusedSingle,
        ScenarioResult FocusedRetry,
        string PrimaryFactor,
        string Reason);
}
