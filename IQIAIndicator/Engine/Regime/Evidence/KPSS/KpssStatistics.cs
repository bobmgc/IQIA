namespace IQIAIndicator.Engine.Regime.Evidence.KPSS;

/// <summary>
/// Calcul de la statistique KPSS et approximation de la p-value.
/// </summary>
internal static class KpssStatistics
{
    // ── Statistique KPSS ────────────────────────────────────────────────

    /// <summary>
    /// Calcule η̂ = T⁻² · Σ_{t=1}^{T} S_t² / ŝ²
    /// où S_t = Σ_{i=1}^{t} ê_i  (somme cumulée des résidus).
    /// Retourne -1 si la variance de long terme est nulle.
    /// </summary>
    internal static decimal ComputeStatistic(decimal[] residuals, int n, decimal longRunVariance)
    {
        if (longRunVariance <= 0m) return -1m;

        decimal sumS2 = 0m, S = 0m;
        for (int t = 0; t < n; t++)
        {
            S     += residuals[t];   // S_t = Σ_{i=0}^{t} ê_i
            sumS2 += S * S;
        }

        // η̂ = (1/T²) · Σ S_t² / ŝ²
        return sumS2 / ((decimal)(n * n) * longRunVariance);
    }

    // ── P-value approximée ───────────────────────────────────────────────

    /// <summary>
    /// P-value approximée du test KPSS par interpolation linéaire sur
    /// 5 points de référence (cv10%, cv5%, cv2.5%, cv1%, ancre à η=0).
    ///
    /// Convention : pour le KPSS, p = P(η̂ &gt; observé | H₀: stationnaire).
    ///   → grande η̂ = petite p = fort rejet de H₀.
    ///
    /// Précision : ±0.03 en p dans l'intervalle [cv10, cv1].
    /// Surface de réponse complète de MacKinnon planifiée au sprint suivant.
    ///
    /// Points d'ancrage asymptotiques :
    ///   η̂ = 0        ↔ p ≈ 0.99  (série parfaitement stationnaire)
    ///   η̂ ≥ cv1%     ↔ p &lt; 0.01  (extrapolation exponentielle)
    /// </summary>
    internal static decimal ApproximatePValue(
        decimal stat, decimal cv1, decimal cv25, decimal cv5, decimal cv10)
    {
        const decimal anchorP0 = 0.99m;  // p quand η̂ = 0

        if (stat <= 0m)
            return anchorP0;

        // En-dessous de cv10% : interpolation vers 0.99 à η̂=0
        if (stat < cv10)
        {
            decimal frac = stat / cv10;
            return anchorP0 - frac * (anchorP0 - 0.10m);
        }

        // Entre cv10% et cv5%
        if (stat < cv5)
            return 0.10m - (stat - cv10) / (cv5 - cv10) * 0.05m;

        // Entre cv5% et cv2.5%
        if (stat < cv25)
            return 0.05m - (stat - cv5) / (cv25 - cv5) * 0.025m;

        // Entre cv2.5% et cv1%
        if (stat < cv1)
            return 0.025m - (stat - cv25) / (cv1 - cv25) * 0.015m;

        // Au-dessus de cv1% : extrapolation exponentielle
        double spread  = (double)(cv1 - cv25);
        double excess  = (double)(stat - cv1);
        double p = 0.01 * Math.Exp(-1.5 * excess / spread);
        return Math.Clamp((decimal)p, 1e-6m, 0.01m);
    }
}
