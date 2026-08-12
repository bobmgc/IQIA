using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.ADF;

// Namespace identique a l'ancien — RegimeEngine inchange
namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Test ADF complet (Augmented Dickey-Fuller).
/// Producteur d'evidence pure : retourne AdfResult sans aucune decision de regime.
/// H0 : racine unitaire. H1 : stationnarite.
/// </summary>
public sealed class AdfEvidence
{
    public AdfResult Compute(EvidenceContext context)
    {
        if (context.SampleSize < context.MinimumSampleSize)
            return AdfResult.Invalid($"Warmup ADF ({context.SampleSize}/{context.MinimumSampleSize} bars).");

        return ComputeAdf(context);
    }

    private static AdfResult ComputeAdf(EvidenceContext context)
    {
        int n = context.SampleSize;
        var series = new decimal[n];
        for (int i = 0; i < n; i++)
            series[i] = context.Series[i];

        // Sprint 15.1 (SCI15-01): explicit, specific diagnostic for the out-of-range-magnitude case,
        // checked upfront so the Explanation names the real cause rather than the generic
        // "regression failed" message that would otherwise result once AdfRegression.TryCompute's
        // own guard (defense in depth, see AdfRegression.cs) rejects it anyway. No statistical
        // formula, threshold, or critical value is touched by this check.
        if (ADF.AdfRegression.HasOutOfRangeMagnitude(series, n))
            return AdfResult.Invalid($"Valeur numerique hors limites representables pour la regression (|x| > {ADF.AdfRegression.SafeMagnitudeBound:E2}).");

        int lag = ADF.AdfStatistics.SelectLag(series, n);

        if (!ADF.AdfRegression.TryCompute(series, n, lag,
                out decimal tStat, out _, out int nObs))
            return AdfResult.Invalid($"Regression ADF echouee (T={n}, p={lag}).");

        var (cv1, cv5, cv10) = ADF.AdfCriticalValues.Get(nObs);
        decimal pv = ADF.AdfStatistics.ApproximatePValue(tStat, cv1, cv5, cv10);

        string expl = tStat < cv5
            ? $"t={tStat:F4}, p={pv:F4}, ADF({lag}) — Rejette H0 @5%"
            : $"t={tStat:F4}, p={pv:F4}, ADF({lag}) — Maintient H0 @5%";

        return new AdfResult
        {
            Statistic       = tStat,
            PValue          = pv,
            Confidence      = Math.Clamp((decimal)n / context.WindowSize, 0m, 1m),
            CriticalValue1  = cv1,
            CriticalValue5  = cv5,
            CriticalValue10 = cv10,
            IsStationary    = tStat < cv5,
            LagUsed         = lag,
            SampleSize      = nObs,
            Explanation     = expl,
            IsValid         = true
        };
    }
}
