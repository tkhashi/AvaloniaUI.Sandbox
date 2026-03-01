namespace ClickVerificationApp.Models;

public sealed record AxElementInfo(
    int Depth,
    string Role,
    string Subrole,
    string Title,
    string Actions,
    double? X,
    double? Y,
    double? Width,
    double? Height)
{
    public override string ToString()
    {
        var indent = new string(' ', Depth * 2);
        var positionText = X.HasValue && Y.HasValue
            ? $"({X.Value:0.##}, {Y.Value:0.##})"
            : "(なし)";
        var sizeText = Width.HasValue && Height.HasValue
            ? $"({Width.Value:0.##}, {Height.Value:0.##})"
            : "(なし)";

        return $"{indent}Role={Role}, Subrole={Subrole}, Title={Title}, Actions={Actions}, Position={positionText}, Size={sizeText}";
    }
}
