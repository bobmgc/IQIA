using IQIAIndicator.Core;

namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Demi-vie du processus d'Ornstein-Uhlenbeck via régression AR(1) sur les variations de prix.
/// Δp_t = α + β × p_{t-1} + ε. β &lt; 0 → retour à la moyenne. HL = ln(2)/(-β).
/// Courte demi-vie → forte mean reversion. Longue → dynamique proche du random walk.
/// </summary>
public sealed class HalfLifeEvidence : IRegimeEvidence
{
    public string ModelName => "HalfLife";

    private const int W    = 30;
    private const int MinN = 20;
    private readonly decimal[] _buf = new decimal[W];
    private int _h, _n;

    public EvidenceResult Compute(MarketContext ctx)
    {
        if (ctx.Clock.IsFirstBar) { _h = _n = 0; Array.Clear(_buf); }

        _buf[_h] = ctx.Price.Close;
        _h = (_h + 1) % W;
        _n = Math.Min(_n + 1, W);

        if (_n < MinN) return Stub();

        // OLS : y = Δp_t, x = p_{t-1}
        int n = _n - 1;
        decimal sumX = 0m, sumY = 0m, sumXY = 0m, sumX2 = 0m;
        for (int i = 0; i < n; i++)
        {
            decimal x = P(i + 1);
            decimal y = P(i) - P(i + 1);
            sumX  += x; sumY  += y;
            sumXY += x * y; sumX2 += x * x;
        }

        decimal meanX = sumX / n;
        decimal meanY = sumY / n;
        decimal den   = sumX2 / n - meanX * meanX;
        if (Math.Abs(den) < 1e-10m) return Stub();

        decimal beta     = sumXY / n - meanX * meanY;
        beta /= den;

        // Demi-vie (valide uniquement si beta < 0)
        decimal halfLife = beta < -1e-8m
            ? (decimal)(Math.Log(2.0) / (double)(-beta))
            : decimal.MaxValue;

        // Score : inversement proportionnel à la demi-vie (normalisée sur [0..1])
        // HL < 5 bars = score 1, HL > 100 bars = score ~0
        var score = beta < 0m
            ? Math.Clamp(1m - (decimal)Math.Log10(Math.Max(1.0, (double)halfLife) / 5.0) / 2m, 0m, 1m)
            : 0m;

        var dir  = beta < -0.01m ? -1 : beta > 0.01m ? 1 : 0;
        var hint = dir < 0 ? RegimeType.MeanReversion
                 : dir > 0 ? (P(0) >= P(_n - 1) ? RegimeType.TrendBull : RegimeType.TrendBear)
                 : RegimeType.Transition;

        return new EvidenceResult
        {
            ModelName   = ModelName,
            Score       = score,
            Confidence  = Math.Clamp((decimal)_n / W, 0m, 1m),
            Direction   = dir,
            RegimeHint  = hint,
            Explanation = halfLife == decimal.MaxValue
                ? $"β={beta:F4} (pas de MR)"
                : $"HL={halfLife:F1} bars",
            IsReady     = true
        };
    }

    private decimal P(int lag) => _buf[(_h - 1 - lag + W) % W];

    private EvidenceResult Stub() => new()
    {
        ModelName = ModelName, Score = 0m, Confidence = 0m,
        Direction = 0, RegimeHint = RegimeType.Unknown,
        Explanation = "Warmup", IsReady = false
    };
}
