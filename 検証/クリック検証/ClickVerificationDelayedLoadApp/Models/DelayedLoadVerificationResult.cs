using System.Collections.Generic;

namespace ClickVerificationDelayedLoadApp.Models;

public sealed record DelayedLoadVerificationResult(
    WindowInfo TargetWindow,
    IReadOnlyList<DelayedLoadAttemptResult> Attempts,
    bool IsLikelyDelayedLoad,
    string VerdictReason,
    int FirstElementCount,
    int LastElementCount,
    int MaxElementCount);
