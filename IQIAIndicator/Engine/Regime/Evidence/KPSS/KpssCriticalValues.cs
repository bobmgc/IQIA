namespace IQIAIndicator.Engine.Regime.Evidence.KPSS;

/// <summary>
/// Valeurs critiques asymptotiques du test KPSS.
///
/// Source : Kwiatkowski, D., Phillips, P.C.B., Schmidt, P., Shin, Y. (1992).
///   "Testing the null hypothesis of stationarity against the alternative of a
///   unit root." Journal of Econometrics, 54(1–3), pp. 159–178. Table 1.
///
/// Ces valeurs sont asymptotiques (T → ∞).
/// Une correction pour échantillons finis (Hobijn et al., 2004) est planifiée.
///
/// Spécification :
///   'c'  → stationnarité en niveau (régression sur constante)
///   'ct' → stationnarité en tendance (régression sur constante + tendance linéaire)
/// </summary>
internal static class KpssCriticalValues
{
    // ── Stationnarité en niveau (μ, regression='c') ──────────────────────
    // Kwiatkowski et al. (1992) Table 1, ligne "η_μ"
    private static readonly (decimal Cv1, decimal Cv25, decimal Cv5, decimal Cv10)
        LevelStationary = (0.739m, 0.574m, 0.463m, 0.347m);

    // ── Stationnarité en tendance (τ, regression='ct') ───────────────────
    // Kwiatkowski et al. (1992) Table 1, ligne "η_τ"
    private static readonly (decimal Cv1, decimal Cv25, decimal Cv5, decimal Cv10)
        TrendStationary = (0.216m, 0.176m, 0.146m, 0.119m);

    // ── Accesseur ────────────────────────────────────────────────────────

    /// <summary>
    /// Retourne (cv1%, cv2.5%, cv5%, cv10%) selon la spécification.
    /// </summary>
    internal static (decimal Cv1, decimal Cv25, decimal Cv5, decimal Cv10) Get(
        bool withTrend = false) =>
        withTrend ? TrendStationary : LevelStationary;
}
