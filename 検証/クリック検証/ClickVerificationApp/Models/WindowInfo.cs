namespace ClickVerificationApp.Models;

public sealed record WindowInfo(
    string AppName,
    string WindowTitle,
    int ProcessId,
    double X,
    double Y)
{
    public override string ToString()
    {
        var titleText = string.IsNullOrWhiteSpace(WindowTitle) ? "(無題)" : WindowTitle;
        return $"{AppName} | {titleText} | 左上=({X:0.##}, {Y:0.##}) | PID={ProcessId}";
    }
}
