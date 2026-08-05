using IQIAIndicator.Core;

namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Demi-vie du processus OU via regression AR(1).
/// Producteur d'evidence pure : retourne HalfLifeResult.
/// beta < 0 -> retour a la moyenne. HL = ln(2)/(-beta).
/// </summary>
public sealed class HalfLifeEvidence
{
    private const int W    = 30;
    private const int MinN = 20;
    private readonly decimal[] _buf = new decimal[W];
    private int _h, _n;

    public HalfLifeResult Compute(MarketContext ctx)
    {
        if (ctx.Clock.IsFirstBar) { _h = _n = 0; Array.Clear(_buf); }
        _buf[_h] = ctx.Price.Close;
        _h = (_h + 1) % W;
        _n = Math.Min(_n + 1, W);
        if (_n < MinN) return HalfLifeResult.Invalid($"Warmup HalfLife ({_n}/{MinN} bars).");

        int n = _n - 1;
        decimal sumX = 0m, sumY = 0m, sumXY = 0m, sumX2 = 0m;
        for (int i = 0; i < n; i++)
        {
            decimal x = P(i + 1);
            decimal y = P(i) - P(i + 1);
            sumX += x; sumY += y; sumXY += x * y; sumX2 += x * x;
        }
        decimal den = sumX2 / n - (sumX / n) * (sumX / n);
        if (Math.Abs(den) < 1e-10m) return HalfLifeResult.Invalid("Matrice singuliere.");

        decimal beta = (sumXY / n - (sumX / n) * (sumY / n)) / den;
        bool mr = beta < -1e-8m;
        decimal hl = mr ? (decimal)(Math.Log(2.0) / (double)(-beta)) : decimal.MaxValue;

        return new HalfLifeResult
        {
            Beta            = beta,
            HalfLifeBars    = Math.Min(hl, 9999m),
            IsMeanReverting = mr,
            Confidence      = Math.Clamp((decimal)_n / W, 0m, 1m),
            IsValid         = true,
            Explanation     = mr ? $"beta={beta:F4}, HL={hl:F1} bars" : $"beta={beta:F4} (pas de MR)"
        };
    }

    private decimal P(int lag) => _buf[(_h - 1 - lag + W) % W];
}
