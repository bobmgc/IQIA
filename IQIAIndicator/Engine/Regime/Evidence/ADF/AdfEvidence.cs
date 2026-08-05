using IQIAIndicator.Core;
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
    private const int W    = 60;
    private const int MinN = 30;

    private readonly decimal[] _buf    = new decimal[W];
    private readonly decimal[] _window = new decimal[W];
    private int _h, _n;

    public AdfResult Compute(MarketContext ctx)
    {
        if (ctx.Clock.IsFirstBar) { _h = _n = 0; Array.Clear(_buf); }

        _buf[_h] = ctx.Price.Close;
        _h = (_h + 1) % W;
        _n = Math.Min(_n + 1, W);

        if (_n < MinN)
            return AdfResult.Invalid($"Warmup ADF ({_n}/{MinN} bars).");

        return ComputeAdf();
    }

    private AdfResult ComputeAdf()
    {
        for (int i = 0; i < _n; i++)
            _window[i] = _buf[(_h - _n + i + W) % W];

        int lag = ADF.AdfStatistics.SelectLag(_window, _n);

        if (!ADF.AdfRegression.TryCompute(_window, _n, lag,
                out decimal tStat, out _, out int nObs))
            return AdfResult.Invalid($"Regression ADF echouee (T={_n}, p={lag}).");

        var (cv1, cv5, cv10) = ADF.AdfCriticalValues.Get(nObs);
        decimal pv = ADF.AdfStatistics.ApproximatePValue(tStat, cv1, cv5, cv10);

        string expl = tStat < cv5
            ? $"t={tStat:F4}, p={pv:F4}, ADF({lag}) — Rejette H0 @5%"
            : $"t={tStat:F4}, p={pv:F4}, ADF({lag}) — Maintient H0 @5%";

        return new AdfResult
        {
            Statistic       = tStat,
            PValue          = pv,
            Confidence      = Math.Clamp((decimal)_n / W, 0m, 1m),
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
