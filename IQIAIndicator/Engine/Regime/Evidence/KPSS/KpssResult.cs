namespace IQIAIndicator.Engine.Regime.Evidence.KPSS;

/// <summary>
/// Résultat interne du test KPSS, avant projection vers EvidenceResult.
/// </summary>
public sealed class KpssResult
{
    public required decimal Statistic        { get; init; }  // η̂ (statistique KPSS)
    public required decimal PValue           { get; init; }  // p-value approximée
    public required decimal CriticalValue1   { get; init; }  // Kwiatkowski et al. (1992) 1%
    public required decimal CriticalValue5   { get; init; }  // 5%
    public required decimal CriticalValue10  { get; init; }  // 10%
    public required bool    IsStationary     { get; init; }  // η̂ < cv5% → H₀ non rejetée
    public required int     Bandwidth        { get; init; }  // largeur de bande Newey-West l
    public required int     SampleSize       { get; init; }  // T effectif
    public required string  Explanation      { get; init; }
    public required bool    IsValid          { get; init; }

    public static KpssResult Invalid(string reason) => new()
    {
        Statistic       = decimal.MaxValue,
        PValue          = 0m,
        CriticalValue1  = 0m,
        CriticalValue5  = 0m,
        CriticalValue10 = 0m,
        IsStationary    = false,
        Bandwidth       = 0,
        SampleSize      = 0,
        Explanation     = reason,
        IsValid         = false
    };
}
