using IQIAIndicator.Core;
using IQIAIndicator.Engine.Regime.Evidence.KPSS;

namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Test KPSS complet (Kwiatkowski-Phillips-Schmidt-Shin).
/// Producteur d'evidence pure : retourne KpssResult sans aucune decision de regime.
/// H0 : stationnarite. H1 : racine unitaire.
/// </summary>
public sealed class KpssEvidence
{
    private const int W    = 60;
    private const int MinN = 30;

    private readonly decimal[] _buf       = new decimal[W];
    private readonly decimal[] _window    = new decimal[W];
    private readonly decimal[] _residuals = new decimal[W];
    private int _h, _n;

    public KpssResult Compute(MarketContext ctx)
    {
        if (ctx.Clock.IsFirstBar) { _h = _n = 0; Array.Clear(_buf); }

        _buf[_h] = ctx.Price.Close;
        _h = (_h + 1) % W;
        _n = Math.Min(_n + 1, W);

        if (_n < MinN)
            return KpssResult.Invalid($"Warmup KPSS ({_n}/{MinN} bars).");

        return ComputeKpss();
    }

    private KpssResult ComputeKpss()
    {
        for (int i = 0; i < _n; i++)
            _window[i] = _buf[(_h - _n + i + W) % W];

        if (!KPSS.KpssRegression.TryDemean(_window, _n, _residuals))
            return KpssResult.Invalid("Demoyennage impossible.");

        decimal lrv = KPSS.KpssLongRunVariance.Compute(_residuals, _n);
        if (lrv <= 0m) return KpssResult.Invalid("Variance de long terme nulle.");

        decimal stat = KPSS.KpssStatistics.ComputeStatistic(_residuals, _n, lrv);
        if (stat < 0m) return KpssResult.Invalid("Statistique eta invalide.");

        var (cv1, cv25, cv5, cv10) = KPSS.KpssCriticalValues.Get(withTrend: false);
        decimal pv = KPSS.KpssStatistics.ApproximatePValue(stat, cv1, cv25, cv5, cv10);
        int bw = KPSS.KpssLongRunVariance.ComputeBandwidth(_n);

        string expl = stat < cv5
            ? $"eta={stat:F4}, p={pv:F4}, KPSS(l={bw}) — Maintient H0 @5%"
            : $"eta={stat:F4}, p={pv:F4}, KPSS(l={bw}) — Rejette H0 @5%";

        return new KpssResult
        {
            Statistic       = stat,
            PValue          = pv,
            Confidence      = Math.Clamp((decimal)_n / W, 0m, 1m),
            CriticalValue1  = cv1,
            CriticalValue5  = cv5,
            CriticalValue10 = cv10,
            IsStationary    = stat < cv5,
            Bandwidth       = bw,
            SampleSize      = _n,
            Explanation     = expl,
            IsValid         = true
        };
    }
}
