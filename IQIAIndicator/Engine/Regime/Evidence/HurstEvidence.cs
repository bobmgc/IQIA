using IQIAIndicator.Core;

namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Proxy Hurst via Variance Ratio a lag 2.
/// Producteur d'evidence pure : retourne HurstResult.
/// VR(2) > 1 -> H > 0.5 -> persistance. VR(2) < 1 -> H < 0.5 -> anti-persistance.
/// </summary>
public sealed class HurstEvidence
{
    private const int W    = 30;
    private const int MinN = 20;
    private readonly decimal[] _buf = new decimal[W];
    private int _h, _n;

    public HurstResult Compute(MarketContext ctx)
    {
        if (ctx.Clock.IsFirstBar) { _h = _n = 0; Array.Clear(_buf); }
        _buf[_h] = ctx.Price.Close;
        _h = (_h + 1) % W;
        _n = Math.Min(_n + 1, W);
        if (_n < MinN) return HurstResult.Invalid($"Warmup Hurst ({_n}/{MinN} bars).");

        int n1 = _n - 1;
        decimal sum1 = 0m, sq1 = 0m;
        for (int i = 0; i < n1; i++) { var r = P(i) - P(i + 1); sum1 += r; sq1 += r * r; }
        decimal mu1 = sum1 / n1;
        decimal v1  = sq1 / n1 - mu1 * mu1;

        int n2 = _n - 2;
        decimal sum2 = 0m, sq2 = 0m;
        for (int i = 0; i < n2; i++) { var r = P(i) - P(i + 2); sum2 += r; sq2 += r * r; }
        decimal mu2 = sum2 / n2;
        decimal v2  = sq2 / n2 - mu2 * mu2;

        if (v1 <= 0m) return HurstResult.Invalid("Variance nulle.");

        decimal vr = v2 / (2m * v1);
        decimal h  = (decimal)((Math.Log((double)Math.Max(vr, 1e-9m)) / Math.Log(2.0) + 1.0) / 2.0);
        h = Math.Clamp(h, 0m, 1m);

        return new HurstResult
        {
            VarianceRatio = vr,
            HurstProxy    = h,
            Confidence    = Math.Clamp((decimal)_n / W, 0m, 1m),
            IsValid       = true,
            Explanation   = $"VR(2)={vr:F4}, H={h:F3}"
        };
    }

    private decimal P(int lag) => _buf[(_h - 1 - lag + W) % W];
}
