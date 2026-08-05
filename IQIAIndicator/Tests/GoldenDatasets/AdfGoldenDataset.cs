namespace IQIAIndicator.Tests.GoldenDatasets;

/// <summary>
/// Séries de référence pour la validation du modèle ADF contre Python Statsmodels.
///
/// Toutes les séries sont générées de façon déterministe (PRNG LCG, seed = 42)
/// pour permettre une comparaison exacte et reproductible.
///
/// ── Protocole de validation ──────────────────────────────────────────────
/// 1. Exécuter le script Python ci-dessous sur chaque série.
/// 2. Renseigner les champs StatsmodeIs* dans chaque AdfGoldenCase.
/// 3. Lancer AdfValidation.RunAll() et vérifier que MAE(τ) &lt; 0.02.
///
/// ── Script Python de référence ──────────────────────────────────────────
/// from statsmodels.tsa.stattools import adfuller
/// result = adfuller(series, autolag='AIC', regression='c')
/// print(f"stat={result[0]:.6f}, pvalue={result[1]:.6f}, lags={result[2]}")
/// </summary>
public static class AdfGoldenDataset
{
    // ── Générateur déterministe (Box-Muller sur LCG 64-bit) ─────────────

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
    /// Bruit blanc i.i.d. N(0,1). Stationnaire par construction (p &lt;&lt; 0.05).
    /// Statsmodels attendu : τ ≈ −10 à −15, p &lt; 0.001.
    /// </summary>
    public static decimal[] WhiteNoise(int n = 100)
    {
        return GenerateGaussian(42UL, n);
    }

    /// <summary>
    /// Marche aléatoire : y_t = y_{t-1} + ε_t. Racine unitaire (p ≈ 0.4–0.9).
    /// Statsmodels attendu : τ ≈ −1 à −2, p &gt; 0.20.
    /// </summary>
    public static decimal[] RandomWalk(int n = 100)
    {
        var eps = GenerateGaussian(42UL, n);
        var y   = new decimal[n];
        y[0]    = 0m;
        for (int i = 1; i < n; i++) y[i] = y[i - 1] + eps[i];
        return y;
    }

    /// <summary>
    /// AR(1) φ=0.5 : y_t = 0.5·y_{t-1} + ε_t. Clairement stationnaire (p &lt; 0.05).
    /// Statsmodels attendu : τ ≈ −5 à −8, p &lt; 0.05.
    /// </summary>
    public static decimal[] Ar1Moderate(int n = 100)
    {
        var eps = GenerateGaussian(42UL, n);
        var y   = new decimal[n];
        for (int i = 1; i < n; i++) y[i] = 0.5m * y[i - 1] + eps[i];
        return y;
    }

    /// <summary>
    /// AR(1) φ=0.95 : quasi racine unitaire. ADF peu puissant ici (p variable).
    /// Statsmodels attendu : τ ≈ −1.5 à −3, p ≈ 0.10–0.50.
    /// </summary>
    public static decimal[] Ar1NearUnitRoot(int n = 100)
    {
        var eps = GenerateGaussian(42UL, n);
        var y   = new decimal[n];
        for (int i = 1; i < n; i++) y[i] = 0.95m * y[i - 1] + eps[i];
        return y;
    }

    /// <summary>
    /// Tendance déterministe + bruit : y_t = 0.01·t + ε_t.
    /// ADF avec constante seule → échoue à rejeter H₀ (p &gt; 0.10).
    /// </summary>
    public static decimal[] DeterministicTrend(int n = 100)
    {
        var eps = GenerateGaussian(42UL, n);
        var y   = new decimal[n];
        for (int i = 0; i < n; i++) y[i] = 0.01m * i + eps[i];
        return y;
    }

    /// <summary>
    /// Processus de retour à la moyenne (Ornstein-Uhlenbeck discret, κ=0.5).
    /// y_t = y_{t-1} + κ·(μ − y_{t-1}) + σ·ε_t, μ=0, σ=1.
    /// Fortement stationnaire, demi-vie ≈ ln(2)/κ ≈ 1.4 bar.
    /// Statsmodels attendu : τ &lt;&lt; −5, p &lt; 0.001.
    /// </summary>
    public static decimal[] MeanReversion(int n = 100)
    {
        var eps = GenerateGaussian(42UL, n);
        var y   = new decimal[n];
        for (int i = 1; i < n; i++)
            y[i] = y[i - 1] + 0.5m * (0m - y[i - 1]) + eps[i];
        return y;
    }

    // ── Résultats de référence (à renseigner après passage sous Python) ───

    /// <summary>
    /// Résultat de référence Python Statsmodels pour une série donnée.
    /// Renseigner après validation externe.
    /// </summary>
    public sealed class StatsmodelsReference
    {
        public required decimal Statistic    { get; init; }
        public required decimal PValue       { get; init; }
        public required int     LagUsed      { get; init; }
        public required bool    IsStationary { get; init; }  // τ < cv5%

        // ── Références connues (à compléter) ──────────────────────────
        // Renseigner avec : adfuller(series, autolag='AIC', regression='c')
        public static readonly StatsmodelsReference WhiteNoise_N100 = new()
        {
            Statistic    = -12.3m,  // placeholder — à remplacer par valeur Python
            PValue       = 0.000m,
            LagUsed      = 0,
            IsStationary = true
        };

        public static readonly StatsmodelsReference RandomWalk_N100 = new()
        {
            Statistic    = -1.7m,   // placeholder
            PValue       = 0.43m,
            LagUsed      = 0,
            IsStationary = false
        };
    }
}
