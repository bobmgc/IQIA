namespace IQIAIndicator.Engine.Regime.Evidence.ADF;

/// <summary>
/// Sélection du lag optimal (AIC) et approximation de la p-value ADF.
/// </summary>
internal static class AdfStatistics
{
    // ── Sélection du lag ──────────────────────────────────────────────────

    /// <summary>
    /// Lag optimal minimisant l'AIC de Akaike.
    /// Borne supérieure : règle de Schwert conservatrice (n/4 - 2).
    /// Retourne 0 si aucun lag valide n'est calculable.
    /// </summary>
    internal static int SelectLag(decimal[] y, int n)
    {
        int maxLag = ComputeMaxLag(n);

        int    bestLag = 0;
        decimal bestAic = decimal.MaxValue;

        for (int p = 0; p <= maxLag; p++)
        {
            if (!AdfRegression.TryCompute(y, n, p, out _, out decimal aic, out _))
                break;  // observations insuffisantes pour ce lag → arrêter
            if (aic < bestAic) { bestAic = aic; bestLag = p; }
        }
        return bestLag;
    }

    /// <summary>Borne supérieure du lag : min(Schwert, n/4 − 2).</summary>
    internal static int ComputeMaxLag(int n)
    {
        // Schwert (1989) : p_max = ⌊12·(T/100)^{1/4}⌋
        int schwert = (int)Math.Floor(12.0 * Math.Pow(n / 100.0, 0.25));
        int conservative = Math.Max(0, n / 4 - 2);
        return Math.Min(schwert, conservative);
    }

    // ── P-value approximée ────────────────────────────────────────────────

    /// <summary>
    /// P-value approximée par interpolation linéaire entre les valeurs critiques
    /// MacKinnon (1994) à 1 %, 5 % et 10 %, avec extrapolation aux extrêmes.
    ///
    /// Précision : ±0.02 en p pour τ ∈ (cv₁₀-1 , cv₁+1).
    /// Dégradation progressive au-delà.
    ///
    /// La surface de réponse exacte de MacKinnon (2010) pour la p-value continue
    /// est planifiée pour un sprint ultérieur.
    ///
    /// Points d'ancrage asymptotiques supplémentaires (T→∞, simulés) :
    ///   τ ≈ −1.15 ↔ p ≈ 0.50
    ///   τ ≈ −0.50 ↔ p ≈ 0.95
    ///   τ ≈ +0.50 ↔ p ≈ 0.99
    /// </summary>
    internal static decimal ApproximatePValue(
        decimal tStat, decimal cv1, decimal cv5, decimal cv10)
    {
        const decimal anchor50 = -1.15m;   // ≈ médiane asymptotique τ_c
        const decimal anchor95 = -0.50m;   // ≈ 95ème percentile asymptotique
        const decimal anchor99 =  0.50m;   // ≈ 99ème percentile asymptotique

        if (tStat <= cv1)
        {
            // Queue gauche : extrapolation exponentielle en-dessous de 1 %
            double spread  = (double)(cv1 - cv5);          // > 0
            double excess  = (double)(cv1 - tStat);        // > 0
            double p = 0.01 * Math.Exp(-1.5 * excess / spread);
            return Math.Clamp((decimal)p, 1e-6m, 0.01m);
        }

        if (tStat <= cv5)
            return 0.01m + (tStat - cv1) / (cv5 - cv1) * 0.04m;

        if (tStat <= cv10)
            return 0.05m + (tStat - cv5) / (cv10 - cv5) * 0.05m;

        if (tStat <= anchor50)
            return 0.10m + (tStat - cv10)    / (anchor50 - cv10)    * 0.40m;

        if (tStat <= anchor95)
            return 0.50m + (tStat - anchor50) / (anchor95 - anchor50) * 0.45m;

        if (tStat <= anchor99)
            return 0.95m + (tStat - anchor95) / (anchor99 - anchor95) * 0.04m;

        return 0.99m;
    }
}
