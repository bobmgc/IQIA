namespace IQIAIndicator.Tests.GoldenDatasets;

/// <summary>
/// Séries de référence pour la validation du test KPSS contre Python Statsmodels.
///
/// ── Script Python de référence ──────────────────────────────────────────
/// from statsmodels.tsa.stattools import kpss
/// stat, pvalue, nlags, crit = kpss(series, regression='c', nlags='legacy')
/// print(f"stat={stat:.6f}, pvalue={pvalue:.6f}, nlags={nlags}")
///
/// Note : Statsmodels plafonne la p-value entre [0.01, 0.10] et retourne
///   "p-value is greater than printed p-value" ou "less than" pour les extrêmes.
///   Utiliser la statistique brute (stat) comme référence primaire de validation.
/// </summary>
public static class KpssGoldenDataset
{
    // ── Générateur déterministe (même que AdfGoldenDataset) ──────────────

    private static decimal[] GenerateGaussian(ulong seed, int n)
    {
        var z = new decimal[n];
        for (int i = 0; i < n; i += 2)
        {
            seed = seed * 6364136223846793005UL + 1442695040888963407UL;
            double u1 = (seed >> 11) / (double)(1UL << 53) + 1e-15;
            seed = seed * 6364136223846793005UL + 1442695040888963407UL;
            double u2 = (seed >> 11) / (double)(1UL << 53);
            double r = Math.Sqrt(-2.0 * Math.Log(u1));
            z[i]     = (decimal)(r * Math.Cos(2.0 * Math.PI * u2));
            if (i + 1 < n)
                z[i + 1] = (decimal)(r * Math.Sin(2.0 * Math.PI * u2));
        }
        return z;
    }

    // ── Séries ───────────────────────────────────────────────────────────

    /// <summary>
    /// Bruit blanc N(0,1). Stationnaire → η̂ &lt;&lt; cv5% (0.463).
    /// Statsmodels attendu : stat &lt; 0.20, p ≈ 0.10 (plafond Statsmodels).
    /// </summary>
    public static decimal[] WhiteNoise(int n = 100) =>
        GenerateGaussian(42UL, n);

    /// <summary>
    /// Marche aléatoire : y_t = y_{t-1} + ε. Racine unitaire → η̂ &gt;&gt; cv1% (0.739).
    /// Statsmodels attendu : stat &gt; 1.0, p ≈ 0.01 (plancher Statsmodels).
    /// </summary>
    public static decimal[] RandomWalk(int n = 100)
    {
        var eps = GenerateGaussian(42UL, n);
        var y   = new decimal[n];
        for (int i = 1; i < n; i++) y[i] = y[i - 1] + eps[i];
        return y;
    }

    /// <summary>
    /// AR(1) φ=0.5. Stationnaire, η̂ &lt; cv5%.
    /// Statsmodels attendu : stat &lt; 0.30, p ≈ 0.10.
    /// </summary>
    public static decimal[] Ar1Moderate(int n = 100)
    {
        var eps = GenerateGaussian(42UL, n);
        var y   = new decimal[n];
        for (int i = 1; i < n; i++) y[i] = 0.5m * y[i - 1] + eps[i];
        return y;
    }

    /// <summary>
    /// AR(1) φ=0.95. Quasi racine unitaire — zone grise pour KPSS.
    /// Statsmodels attendu : stat entre 0.30 et 0.80 (variable selon l).
    /// </summary>
    public static decimal[] Ar1NearUnitRoot(int n = 100)
    {
        var eps = GenerateGaussian(42UL, n);
        var y   = new decimal[n];
        for (int i = 1; i < n; i++) y[i] = 0.95m * y[i - 1] + eps[i];
        return y;
    }

    /// <summary>
    /// Tendance déterministe y_t = 0.01·t + ε. Non-stationnaire en niveau.
    /// KPSS (regression='c') doit rejeter H₀ → η̂ &gt; cv5%.
    /// </summary>
    public static decimal[] DeterministicTrend(int n = 100)
    {
        var eps = GenerateGaussian(42UL, n);
        var y   = new decimal[n];
        for (int i = 0; i < n; i++) y[i] = 0.01m * i + eps[i];
        return y;
    }

    /// <summary>
    /// Processus OU (κ=0.5). Fortement stationnaire → η̂ &lt;&lt; cv10% (0.347).
    /// </summary>
    public static decimal[] MeanReversion(int n = 100)
    {
        var eps = GenerateGaussian(42UL, n);
        var y   = new decimal[n];
        for (int i = 1; i < n; i++)
            y[i] = y[i - 1] + 0.5m * (0m - y[i - 1]) + eps[i];
        return y;
    }

    // ── Résultats de référence (à compléter après Python) ────────────────

    public sealed class StatsmodelsReference
    {
        public required decimal Statistic    { get; init; }
        public required decimal PValue       { get; init; }  // plafonné [0.01, 0.10] par Statsmodels
        public required int     NLags        { get; init; }  // largeur de bande appliquée
        public required bool    IsStationary { get; init; }  // stat < cv5%

        // Placeholders — à remplacer par les valeurs Python après validation
        public static readonly StatsmodelsReference WhiteNoise_N100 = new()
        {
            Statistic    = 0.09m,   // placeholder
            PValue       = 0.10m,
            NLags        = 7,       // ceil(12*(100/100)^0.25) = 12 → mais Statsmodels utilise parfois moins
            IsStationary = true
        };

        public static readonly StatsmodelsReference RandomWalk_N100 = new()
        {
            Statistic    = 1.50m,   // placeholder — valeur typique pour une RW de T=100
            PValue       = 0.01m,
            NLags        = 7,
            IsStationary = false
        };
    }
}
