namespace IQIAIndicator.Engine.Regime.Evidence.KPSS;

/// <summary>
/// Calcul des résidus de détrending utilisés par le test KPSS.
///
/// Spécification 'c'  (niveau) : ê_t = y_t − ȳ
/// Spécification 'ct' (tendance) : ê_t = y_t − â − b̂·t  (MCO de y sur [1, t])
///
/// Le tableau en sortie est oldest-first, de taille n.
/// Retourne false si le calcul échoue (variance nulle, diviseur nul, etc.).
/// </summary>
internal static class KpssRegression
{
    /// <summary>
    /// Calcul des résidus pour la spécification niveau ('c').
    /// y : série oldest-first.
    /// </summary>
    internal static bool TryDemean(decimal[] y, int n, decimal[] residuals)
    {
        if (n < 2) return false;

        decimal mean = 0m;
        for (int t = 0; t < n; t++) mean += y[t];
        mean /= n;

        for (int t = 0; t < n; t++)
            residuals[t] = y[t] - mean;

        return true;
    }

    /// <summary>
    /// Calcul des résidus pour la spécification tendance ('ct').
    /// Régression MCO de y sur [1, t] via formules analytiques (pas d'inversion matricielle).
    /// t prend les valeurs 0, 1, ..., n−1 (temps normalisé oldest = 0).
    /// </summary>
    internal static bool TryDetrend(decimal[] y, int n, decimal[] residuals)
    {
        if (n < 4) return false;   // besoin d'au moins 4 points pour une régression significative

        decimal sumT = 0m, sumY = 0m, sumTY = 0m, sumT2 = 0m;
        for (int t = 0; t < n; t++)
        {
            decimal dt = t;
            sumT  += dt;
            sumY  += y[t];
            sumTY += dt * y[t];
            sumT2 += dt * dt;
        }

        // Pente b̂ (MCO analytique)
        decimal denom = n * sumT2 - sumT * sumT;
        if (Math.Abs(denom) < 1e-15m) return false;

        decimal bHat = (n * sumTY - sumT * sumY) / denom;
        decimal aHat = (sumY - bHat * sumT) / n;

        for (int t = 0; t < n; t++)
            residuals[t] = y[t] - aHat - bHat * t;

        return true;
    }
}
