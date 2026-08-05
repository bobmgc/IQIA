namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Résultat brut du test de Lo-MacKinlay (Variance Ratio à lag 5).
/// Aucune décision de régime — observation statistique uniquement.
/// </summary>
public sealed class VarianceRatioResult
{
    public required decimal VR5         { get; init; }  // VR(5) = Var(5-step) / (5·Var(1-step))
    public required decimal Confidence  { get; init; }
    public required bool    IsValid     { get; init; }
    public required string  Explanation { get; init; }

    public static VarianceRatioResult Invalid(string reason) => new()
    {
        VR5 = 1m, Confidence = 0m, IsValid = false, Explanation = reason
    };
}
