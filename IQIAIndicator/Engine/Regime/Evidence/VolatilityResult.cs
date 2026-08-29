namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Résultat brut du test de clustering de volatilité (autocorrélation de |rendements|).
/// Aucune décision de régime — observation statistique uniquement.
/// </summary>
public sealed class VolatilityResult
{
    public required decimal AcfAbsReturns { get; init; }  // ACF(|r|) au lag 1
    public required bool    IsClustering  { get; init; }  // ACF > 0 → clustering présent
    public required decimal Confidence    { get; init; }
    public required bool    IsValid       { get; init; }
    public required string  Explanation   { get; init; }

    public static VolatilityResult Invalid(string reason) => new()
    {
        AcfAbsReturns = 0m, IsClustering = false,
        Confidence = 0m, IsValid = false, Explanation = reason
    };
}
