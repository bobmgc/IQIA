namespace IQIAIndicator.Engine.Regime.Evidence.KPSS;

/// <summary>
/// Estimateur de la variance de long terme par la méthode Newey-West (1987)
/// avec noyau de Bartlett.
///
/// Formule :
///   ŝ² = γ̂₀ + 2 · Σ_{j=1}^{l} w(j,l) · γ̂ⱼ
///
/// où γ̂ⱼ = (1/T) · Σ_{t=j+1}^{T} ê_t · ê_{t-j}   (autocovariance au lag j)
///       w(j,l) = 1 − j/(l+1)                         (noyau de Bartlett)
///
/// Largeur de bande l :
///   l = ⌈12 · (T/100)^{1/4}⌉   (identique à Statsmodels kpss, nlags='legacy')
///
/// Source : Newey, W.K., West, K.D. (1987). "A Simple, Positive Semi-definite,
///   Heteroskedasticity and Autocorrelation Consistent Covariance Matrix."
///   Econometrica, 55(3), pp. 703–708.
/// </summary>
internal static class KpssLongRunVariance
{
    /// <summary>
    /// Calcule la variance de long terme des résidus (tableau oldest-first).
    /// Retourne −1 si le calcul est impossible.
    /// </summary>
    internal static decimal Compute(decimal[] residuals, int n)
    {
        if (n < 3) return -1m;

        int l = ComputeBandwidth(n);

        // γ̂₀ = (1/T) · Σ ê_t²
        decimal gamma0 = 0m;
        for (int t = 0; t < n; t++)
            gamma0 += residuals[t] * residuals[t];
        gamma0 /= n;

        if (gamma0 == 0m) return 0m;

        // Ajouter les autocovariances pondérées
        decimal lrv = gamma0;
        for (int j = 1; j <= l; j++)
        {
            decimal gammaJ = 0m;
            for (int t = j; t < n; t++)
                gammaJ += residuals[t] * residuals[t - j];
            gammaJ /= n;

            // Poids de Bartlett : w(j,l) = 1 − j/(l+1)
            decimal weight = 1m - (decimal)j / (l + 1);
            lrv += 2m * weight * gammaJ;
        }

        return lrv > 0m ? lrv : gamma0;  // garde-fou : jamais négatif
    }

    /// <summary>
    /// Largeur de bande : l = ⌈12 · (T/100)^{1/4}⌉.
    /// Identique à Statsmodels (kpss, nlags='legacy').
    /// </summary>
    internal static int ComputeBandwidth(int n) =>
        (int)Math.Ceiling(12.0 * Math.Pow(n / 100.0, 0.25));
}
