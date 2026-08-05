namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Résultat brut de l'estimation de la demi-vie via régression AR(1) (proxy OU).
/// Aucune décision de régime — observation statistique uniquement.
/// </summary>
public sealed class HalfLifeResult
{
    public required decimal Beta           { get; init; }  // coefficient AR(1) sur y_{t-1}
    public required decimal HalfLifeBars   { get; init; }  // ln(2)/(-β), valide seulement si β < 0
    public required bool    IsMeanReverting { get; init; }  // β < 0
    public required decimal Confidence     { get; init; }  // 0..1
    public required bool    IsValid        { get; init; }
    public required string  Explanation    { get; init; }

    public static HalfLifeResult Invalid(string reason) => new()
    {
        Beta = 0m, HalfLifeBars = decimal.MaxValue,
        IsMeanReverting = false, Confidence = 0m, IsValid = false, Explanation = reason
    };
}
