using IQIAIndicator.Core;

namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Test de Lo-MacKinlay (Variance Ratio a lag 5).
/// Producteur d'evidence pure : retourne VarianceRatioResult.
/// VR(5) > 1 -> autocorrelation positive. VR(5) < 1 -> autocorrelation negative.
/// </summary>
public sealed class VarianceRatioEvidence
{
    private const int W    = 30;
    private const int MinN = 20;
    private readonly decimal[] _buf = new decimal[W];
    private int _h, _n;

    public VarianceRatioResult Compute(MarketContext ctx)
    {
        if (ctx.Clock.IsFirstBar) { _h = _n = 0; Array.Clear(_buf); }
        _buf[_h] = ctx.Price.Close;
        _h = (_h + 1) % W;
        _n = Math.Min(_n + 1, W);
        if (_n < MinN) return VarianceRatioResult.Invalid($"Warmup VR ({_n}/{MinN} bars).");

        int n1 = _n - 1;
        decimal s1 = 0m, sq1 = 0m;
        for (int i = 0; i < n1; i++) { var r = P(i) - P(i + 1); s1 += r; sq1 += r * r; }
        decimal m1 = s1 / n1;
        decimal v1 = sq1 / n1 - m1 * m1;

        int n5 = _n - 5;
        if (n5 < 5) return VarianceRatioResult.Invalid("Pas assez de points pour lag 5.");
        decimal s5 = 0m, sq5 = 0m;
        for (int i = 0; i < n5; i++) { var r = P(i) - P(i + 5); s5 += r; sq5 += r * r; }
        decimal m5 = s5 / n5;
        decimal v5 = sq5 / n5 - m5 * m5;

        if (v1 <= 0m) return VarianceRatioResult.Invalid("Variance nulle.");

        decimal vr5 = v5 / (5m * v1);

        return new VarianceRatioResult
        {
            VR5         = vr5,
            Confidence  = Math.Clamp((decimal)_n / W, 0m, 1m),
            IsValid     = true,
            Explanation = $"VR(5)={vr5:F4}"
        };
    }

    private decimal P(int lag) => _buf[(_h - 1 - lag + W) % W];
}
