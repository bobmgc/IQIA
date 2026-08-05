using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.KPSS;

namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Test KPSS complet (Kwiatkowski-Phillips-Schmidt-Shin).
/// Producteur d'evidence pure : retourne KpssResult sans aucune decision de regime.
/// H0 : stationnarite. H1 : racine unitaire.
/// </summary>
public sealed class KpssEvidence
{
    public KpssResult Compute(EvidenceContext context)
    {
        if (context.SampleSize < context.MinimumSampleSize)
            return KpssResult.Invalid($"Warmup KPSS ({context.SampleSize}/{context.MinimumSampleSize} bars).");

        return ComputeKpss(context);
    }

    private static KpssResult ComputeKpss(EvidenceContext context)
    {
        int n = context.SampleSize;
        var series = new decimal[n];
        for (int i = 0; i < n; i++)
            series[i] = context.Series[i];

        var residuals = new decimal[n];

        if (!KPSS.KpssRegression.TryDemean(series, n, residuals))
            return KpssResult.Invalid("Demoyennage impossible.");

        decimal lrv = KPSS.KpssLongRunVariance.Compute(residuals, n);
        if (lrv <= 0m) return KpssResult.Invalid("Variance de long terme nulle.");

        decimal stat = KPSS.KpssStatistics.ComputeStatistic(residuals, n, lrv);
        if (stat < 0m) return KpssResult.Invalid("Statistique eta invalide.");

        var (cv1, cv25, cv5, cv10) = KPSS.KpssCriticalValues.Get(withTrend: false);
        decimal pv = KPSS.KpssStatistics.ApproximatePValue(stat, cv1, cv25, cv5, cv10);
        int bw = KPSS.KpssLongRunVariance.ComputeBandwidth(n);

        string expl = stat < cv5
            ? $"eta={stat:F4}, p={pv:F4}, KPSS(l={bw}) — Maintient H0 @5%"
            : $"eta={stat:F4}, p={pv:F4}, KPSS(l={bw}) — Rejette H0 @5%";

        return new KpssResult
        {
            Statistic       = stat,
            PValue          = pv,
            Confidence      = Math.Clamp((decimal)n / context.WindowSize, 0m, 1m),
            CriticalValue1  = cv1,
            CriticalValue5  = cv5,
            CriticalValue10 = cv10,
            IsStationary    = stat < cv5,
            Bandwidth       = bw,
            SampleSize      = n,
            Explanation     = expl,
            IsValid         = true
        };
    }
}
