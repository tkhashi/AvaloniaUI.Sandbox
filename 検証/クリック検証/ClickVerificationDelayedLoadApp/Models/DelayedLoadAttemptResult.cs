namespace ClickVerificationDelayedLoadApp.Models;

public sealed record DelayedLoadAttemptResult(
    int Attempt,
    int ElapsedMilliseconds,
    int TotalElements,
    int WebAreaCount,
    int UniqueSignatureCount,
    bool TimedOut,
    string ErrorMessage)
{
    public override string ToString()
    {
        if (TimedOut)
        {
            return $"[{Attempt}] {ElapsedMilliseconds}ms: Timeout";
        }

        if (!string.IsNullOrWhiteSpace(ErrorMessage))
        {
            return $"[{Attempt}] {ElapsedMilliseconds}ms: Error={ErrorMessage}";
        }

        return $"[{Attempt}] {ElapsedMilliseconds}ms: elements={TotalElements}, webAreas={WebAreaCount}, unique={UniqueSignatureCount}";
    }
}
