using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.DFA;

// Namespace identique aux autres modèles — RegimeEngine inchangé
namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// DFA-1 (Detrended Fluctuation Analysis, ordre 1) appliqué aux log-rendements.
///
/// Pipeline :
///   1. Série de prix oldest-first fournie par le contexte.
///   2. Log-rendements : r_t = ln(P_t / P_{t-1}).
///   3. Profil intégré : Y(k) = Σ_{i=1}^k (r_i − r̄).
///   4. Tailles de fenêtres logarithmiquement espacées.
///   5. F(n) = sqrt( mean_v [ (1/n) Σ résidus² ] ) — passes avant et arrière.
///   6. Régression log(F) ~ log(n) → exposant de Hurst H.
///
/// Producteur d'évidence pure : retourne DfaResult sans aucune décision de régime.
/// </summary>
public sealed class DfaEvidence
{
    // ── Point d'entrée ────────────────────────────────────────────────────────

    public DfaResult Compute(EvidenceContext context)
    {
        if (context.SampleSize < context.MinimumSampleSize)
            return DfaResult.Invalid($"Warmup DFA ({context.SampleSize}/{context.MinimumSampleSize} bars).");

        return ComputeDfa(context);
    }

    // ── Pipeline DFA ──────────────────────────────────────────────────────────

    private static DfaResult ComputeDfa(EvidenceContext context)
    {
        int n = context.SampleSize;
        var window = new double[n];
        for (int i = 0; i < n; i++)
            window[i] = (double)context.Series[i];

        // 2. Log-rendements
        int nR = n - 1;
        var returns = new double[nR];
        if (!DfaMath.TryLogReturns(window, n, returns))
            return DfaResult.Invalid("Prix invalides (≤ 0).");

        // 3. Profil intégré des rendements démoyennés
        var profile = new double[nR];
        double meanR = DfaMath.Mean(returns, nR);
        DfaMath.IntegrateProfile(returns, nR, meanR, profile);

        // 4. Tailles de fenêtres
        int[] sizes = DfaStatistics.GenerateWindowSizes(nR);
        if (sizes.Length < 4)
            return DfaResult.Invalid($"Série trop courte pour le DFA (T={nR}).");

        // 5. F(n) pour chaque taille (filtre les NaN et négatifs)
        int valid = 0;
        var validSizes = new int[sizes.Length];
        var validFlucts = new double[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            double f = DfaStatistics.ComputeFluctuation(profile, nR, sizes[i]);
            if (double.IsNaN(f) || f <= 0.0) continue;
            validSizes[valid]  = sizes[i];
            validFlucts[valid] = f;
            valid++;
        }

        if (valid < 4)
            return DfaResult.Invalid("Trop peu de fenêtres valides après filtrage.");

        // 6. Régression log-log → H
        if (!DfaStatistics.EstimateHurst(validSizes, validFlucts, valid,
                out double hurst, out double r2))
            return DfaResult.Invalid("Régression log-log échouée.");

        hurst = Math.Clamp(hurst, 0.0, 2.0);

        // 7. Confiance : R² × couverture des fenêtres × taille d'échantillon
        double confidence = r2
            * Math.Min(1.0, valid / 6.0)
            * Math.Min(1.0, n / (double)context.WindowSize);

        // 8. Snapshots pour DfaResult (allocation unique hors boucle chaude)
        var winArr   = new double[valid];
        var fluctArr = new double[valid];
        for (int i = 0; i < valid; i++)
        {
            winArr[i]   = validSizes[i];
            fluctArr[i] = validFlucts[i];
        }

        return new DfaResult
        {
            Hurst        = hurst,
            RSquared     = r2,
            Confidence   = confidence,
            WindowCount  = valid,
            IsValid      = true,
            WindowSizes  = winArr,
            Fluctuations = fluctArr,
            Explanation  = $"H={hurst:F4}, R²={r2:F3}, {valid} fenêtres, T={nR}"
        };
    }
}
