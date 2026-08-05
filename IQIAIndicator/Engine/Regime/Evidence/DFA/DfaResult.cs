namespace IQIAIndicator.Engine.Regime.Evidence.DFA;

/// <summary>
/// Résultat brut du DFA (Detrended Fluctuation Analysis).
/// Aucune décision de régime — observation statistique uniquement.
///
/// Interprétation de l'exposant de Hurst H :
///   H ≈ 0.50 → marche aléatoire (mémoire nulle)
///   H &gt; 0.50 → persistance (tendance à long terme)
///   H &lt; 0.50 → anti-persistance (retour à la moyenne)
/// </summary>
public sealed class DfaResult
{
    public required double   Hurst        { get; init; }  // exposant de Hurst [0..2]
    public required double   RSquared     { get; init; }  // R² de la régression log-log [0..1]
    public required double   Confidence   { get; init; }  // confiance composite [0..1]
    public required int      WindowCount  { get; init; }  // fenêtres valides utilisées
    public required bool     IsValid      { get; init; }
    public required string   Explanation  { get; init; }

    // Séries log-log pour visualisation ou débogage externe
    public IReadOnlyList<double> WindowSizes  { get; init; } = [];
    public IReadOnlyList<double> Fluctuations { get; init; } = [];

    public static DfaResult Invalid(string reason) => new()
    {
        Hurst = 0.5, RSquared = 0.0, Confidence = 0.0,
        WindowCount = 0, IsValid = false, Explanation = reason
    };
}
