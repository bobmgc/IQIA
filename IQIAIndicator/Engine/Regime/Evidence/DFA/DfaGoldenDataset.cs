namespace IQIAIndicator.Engine.Regime.Evidence.DFA;

/// <summary>
/// Séries de référence pour la validation du DFA IQIA contre Python (nolds).
///
/// Toutes les séries sont déterministes (PRNG LCG Box-Muller, seed = 42).
///
/// ── Script Python de référence ──────────────────────────────────────────────
/// import nolds, numpy as np
///
/// def lcg_gaussian(seed, n):
///     """Même PRNG que C# pour reproductibilité."""
///     rng = np.random.default_rng(seed)        # différent du LCG C#
///     return rng.standard_normal(n)             # approximation acceptable
///
/// # Référence exacte : utiliser les séries générées par C# exportées en CSV.
/// for name, series in datasets.items():
///     h = nolds.dfa(series)
///     print(f"{name}: H = {h:.4f}")
///
/// Note : nolds utilise fit_exp='RANSAC' par défaut (robuste).
/// Pour comparer avec notre OLS : nolds.dfa(series, fit_exp='poly')
/// </summary>
public static class DfaGoldenDataset
{
    // ── Générateur déterministe (identique ADF/KPSS) ─────────────────────────

    private static double[] GenerateGaussian(ulong seed, int n)
    {
        var z = new double[n];
        for (int i = 0; i < n; i += 2)
        {
            seed = seed * 6364136223846793005UL + 1442695040888963407UL;
            double u1 = (seed >> 11) / (double)(1UL << 53) + 1e-15;
            seed = seed * 6364136223846793005UL + 1442695040888963407UL;
            double u2 = (seed >> 11) / (double)(1UL << 53);
            double r  = Math.Sqrt(-2.0 * Math.Log(u1));
            z[i]      = r * Math.Cos(2.0 * Math.PI * u2);
            if (i + 1 < n) z[i + 1] = r * Math.Sin(2.0 * Math.PI * u2);
        }
        return z;
    }

    // ── Séries ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Bruit blanc N(0,1).
    /// DFA appliqué au profil de z → H ≈ 0.50.
    /// nolds.dfa(z, fit_exp='poly') attendu : 0.45 – 0.55.
    /// </summary>
    public static double[] WhiteNoise(int n = 256) => GenerateGaussian(42UL, n);

    /// <summary>
    /// Marche aléatoire en PRIX ; log-rendements = bruit blanc.
    /// DFA sur log-rendements → H ≈ 0.50.
    /// nolds.dfa(log_returns) attendu : 0.45 – 0.55.
    /// </summary>
    public static double[] RandomWalkPrices(int n = 256)
    {
        var z = GenerateGaussian(42UL, n);
        var p = new double[n];
        p[0] = 100.0;
        for (int i = 1; i < n; i++) p[i] = p[i - 1] * Math.Exp(z[i] * 0.01);
        return p;
    }

    /// <summary>
    /// Série persistante : AR(1) φ=0.90 sur les rendements.
    /// Mémoire à court terme positive → H &gt; 0.50.
    /// nolds.dfa attendu : 0.60 – 0.80.
    /// </summary>
    public static double[] PersistentAr1(int n = 256)
    {
        var z = GenerateGaussian(42UL, n);
        var y = new double[n];
        for (int i = 1; i < n; i++) y[i] = 0.9 * y[i - 1] + z[i];
        return y;
    }

    /// <summary>
    /// Processus OU discret (κ=0.5) — fortement anti-persistant.
    /// θ_t = θ_{t-1} + κ(0 − θ_{t-1}) + ε_t
    /// nolds.dfa attendu : 0.25 – 0.45.
    /// </summary>
    public static double[] OuProcess(int n = 256)
    {
        var z = GenerateGaussian(42UL, n);
        var y = new double[n];
        for (int i = 1; i < n; i++)
            y[i] = y[i - 1] + 0.5 * (0.0 - y[i - 1]) + z[i];
        return y;
    }

    /// <summary>
    /// fBm synthétique (proxy via accumulation d'AR(1) à longue mémoire).
    /// AR(1) φ=0.5 avec drift long → H entre 0.55 et 0.70.
    /// Pour fBm exact (H=0.7), utiliser une bibliothèque dédiée sous Python.
    /// </summary>
    public static double[] FractionalBrownianProxy(int n = 256)
    {
        var z = GenerateGaussian(42UL, n);
        var y = new double[n];
        for (int i = 1; i < n; i++) y[i] = 0.5 * y[i - 1] + z[i];
        return y;
    }

    /// <summary>
    /// Anti-persistant fort : AR(1) φ=−0.80.
    /// Alternance rapide → H &lt;&lt; 0.5.
    /// nolds.dfa attendu : 0.15 – 0.35.
    /// </summary>
    public static double[] AntiPersistentAr1(int n = 256)
    {
        var z = GenerateGaussian(42UL, n);
        var y = new double[n];
        for (int i = 1; i < n; i++) y[i] = -0.8 * y[i - 1] + z[i];
        return y;
    }

    // ── Valeurs de référence Python (à renseigner après validation) ──────────

    public sealed class NoldsReference
    {
        public required double H            { get; init; }  // nolds.dfa(series, fit_exp='poly')
        public required double HRansac      { get; init; }  // nolds.dfa(series) — fit_exp='RANSAC'
        public required string SeriesName   { get; init; }

        // Placeholders — remplacer par les valeurs Python après validation
        public static readonly NoldsReference WhiteNoise_N256 = new()
        {
            SeriesName = "WhiteNoise_N256", H = 0.50, HRansac = 0.50
        };
        public static readonly NoldsReference Persistent_N256 = new()
        {
            SeriesName = "PersistentAr1_N256", H = 0.70, HRansac = 0.70
        };
        public static readonly NoldsReference OuProcess_N256 = new()
        {
            SeriesName = "OuProcess_N256", H = 0.38, HRansac = 0.38
        };
    }
}
