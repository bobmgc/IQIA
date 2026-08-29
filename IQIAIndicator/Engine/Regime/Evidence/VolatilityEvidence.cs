using IQIAIndicator.Core;

namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Clustering de volatilite via autocorrelation des |rendements|.
/// Producteur d'evidence pure : retourne VolatilityResult.
/// ACF > 0 -> effet ARCH present. ACF ~ 0 -> pas de clustering.
/// </summary>
public sealed class VolatilityEvidence
{
    private const int W    = 30;
    private const int MinN = 20;
    private readonly decimal[] _buf = new decimal[W];
    private int _h, _n;

    public VolatilityResult Compute(MarketContext ctx)
    {
        if (ctx.Clock.IsFirstBar) { _h = _n = 0; Array.Clear(_buf); }
        _buf[_h] = ctx.Price.Close;
        _h = (_h + 1) % W;
        _n = Math.Min(_n + 1, W);
        if (_n < MinN) return VolatilityResult.Invalid($"Warmup Volatility ({_n}/{MinN} bars).");

        int n = _n - 1;
        decimal sumA = 0m;
        for (int i = 0; i < n; i++) sumA += Math.Abs(P(i) - P(i + 1));
        decimal mu = sumA / n;

        decimal cov = 0m, varA = 0m;
        for (int i = 0; i < n - 1; i++)
        {
            decimal a0 = Math.Abs(P(i)     - P(i + 1)) - mu;
            decimal a1 = Math.Abs(P(i + 1) - P(i + 2)) - mu;
            cov  += a0 * a1;
            varA += a0 * a0;
        }
        if (varA == 0m) return VolatilityResult.Invalid("Variance nulle.");

        decimal acf = cov / varA;

        return new VolatilityResult
        {
            AcfAbsReturns = acf,
            IsClustering  = acf > 0.05m,
            Confidence    = Math.Clamp((decimal)_n / W, 0m, 1m),
            IsValid       = true,
            Explanation   = $"ACF(|r|)={acf:F4}, clustering={acf > 0.05m}"
        };
    }

    private decimal P(int lag) => _buf[(_h - 1 - lag + W) % W];
}
