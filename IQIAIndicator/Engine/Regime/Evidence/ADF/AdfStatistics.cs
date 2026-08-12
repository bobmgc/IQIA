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
    ///
    /// Sprint 15.2 (ADF-SCALE): l'AIC de chaque candidat p est calculé sur un échantillon tronqué
    /// à un nombre d'observations constant (n − maxLag − 1) pour tous les p, au lieu de l'échantillon
    /// naturel (n − 1 − p, qui varie avec p car un lag plus grand consomme plus d'observations
    /// initiales). Cette troncature commune ne change ni la formule AIC ni la régression OLS
    /// elles-mêmes (AdfRegression.cs est inchangé) : c'est la pratique standard (cf. statsmodels
    /// adfuller(autolag='AIC'), Ng &amp; Perron) pour rendre des AIC comparables entre modèles à p
    /// différent, seule façon valide de les classer par argmin.
    ///
    /// Cette troncature élimine aussi, comme conséquence mathématique directe et non comme correctif
    /// ad hoc, la scale-sensitivity trouvée au Sprint 15.1 (RandomWalk(100) : lag=0 à l'échelle 1,
    /// lag=12 à l'échelle 10). Preuve : sous y' = c·y, SSR'(p) = c²·SSR(p) exactement (OLS), donc
    /// AIC'(p) = nObs(p)·ln(c²) + AIC(p). Avec l'ancien nObs(p) = n−1−p (variable selon p), ce
    /// décalage diffère d'un candidat à l'autre et peut inverser l'argmin. Avec nObs fixé à la même
    /// valeur pour tous les p, le décalage nObs·ln(c²) est une CONSTANTE additive identique pour tous
    /// les candidats : elle s'annule exactement dans la comparaison argmin, pour tout c &gt; 0 — pas
    /// une tolérance, une propriété algébrique exacte. Vérifié empiriquement sur les 6 datasets
    /// canoniques (aucun changement de lag à l'échelle 1) et sur un balayage d'échelle 0.01×–1000× sur
    /// 5 familles de séries (voir AdfLagSelectionScaleStabilityTests).
    ///
    /// La régression finale du lag retenu (appelée séparément par AdfEvidence/AdfValidation avec la
    /// série complète, pas cette troncature) est inchangée : Statistic/PValue/SampleSize restent
    /// calculés exactement comme avant pour un lag donné.
    /// </summary>
    internal static int SelectLag(decimal[] y, int n)
    {
        int maxLag = ComputeMaxLag(n);

        int     bestLag = 0;
        decimal bestAic = decimal.MaxValue;

        for (int p = 0; p <= maxLag; p++)
        {
            // Échantillon commun : on tronque le début de la série de sorte que la variable
            // dépendante (Δy) couvre exactement les mêmes observations pour tout p — seules les
            // p premières différences retardées utilisées comme régresseurs diffèrent.
            int trim    = maxLag - p;
            int nSliced = n - trim;
            var sliced  = new decimal[nSliced];
            Array.Copy(y, trim, sliced, 0, nSliced);

            if (!AdfRegression.TryCompute(sliced, nSliced, p, out _, out decimal aic, out _))
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
