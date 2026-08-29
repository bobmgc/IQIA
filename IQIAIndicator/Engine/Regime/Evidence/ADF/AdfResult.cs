namespace IQIAIndicator.Engine.Regime.Evidence.ADF;

/// <summary>
/// Résultat interne du calcul ADF, avant projection vers EvidenceResult.
/// Contient toutes les métriques intermédiaires pour traçabilité et validation.
/// </summary>
public sealed class AdfResult
{
    public required decimal Statistic        { get; init; }  // τ (t-stat sur y_{t-1})
    public required decimal PValue           { get; init; }  // p-value approximée
    public required decimal Confidence       { get; init; }  // 0..1, basée sur la taille d'échantillon
    public required decimal CriticalValue1   { get; init; }  // MacKinnon 1%
    public required decimal CriticalValue5   { get; init; }  // MacKinnon 5%
    public required decimal CriticalValue10  { get; init; }  // MacKinnon 10%
    public required bool    IsStationary     { get; init; }  // τ < CV5% → rejette H₀
    public required int     LagUsed          { get; init; }  // p optimal (AIC)
    public required int     SampleSize       { get; init; }  // T effectif
    public required string  Explanation      { get; init; }
    public required bool    IsValid          { get; init; }

    /// <summary>Résultat invalide lorsque le calcul ne peut pas aboutir.</summary>
    public static AdfResult Invalid(string reason) => new()
    {
        Statistic       = 0m,
        PValue          = 1m,
        Confidence      = 0m,
        CriticalValue1  = 0m,
        CriticalValue5  = 0m,
        CriticalValue10 = 0m,
        IsStationary    = false,
        LagUsed         = 0,
        SampleSize      = 0,
        Explanation     = reason,
        IsValid         = false
    };
}
