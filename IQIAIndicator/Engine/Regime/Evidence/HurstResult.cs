namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Résultat brut du proxy Hurst (Variance Ratio à lag 2).
/// Aucune décision de régime — observation statistique uniquement.
/// </summary>
public sealed class HurstResult
{
    public required decimal VarianceRatio { get; init; }  // VR(2) = Var(2-step) / (2·Var(1-step))
    public required decimal HurstProxy   { get; init; }  // H ≈ (log(VR)/log(2) + 1) / 2
    public required decimal Confidence   { get; init; }  // 0..1
    public required bool    IsValid      { get; init; }
    public required string  Explanation  { get; init; }

    public static HurstResult Invalid(string reason) => new()
    {
        VarianceRatio = 0m, HurstProxy = 0.5m,
        Confidence = 0m, IsValid = false, Explanation = reason
    };
}
