using IQIAIndicator.Core;

namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Proxy exposant de Hurst via le Variance Ratio à lag q=2.
/// VR(2) = Var(2-step) / (2 × Var(1-step)).
/// VR > 1 → H > 0.5 → persistance → tendance. VR &lt; 1 → H &lt; 0.5 → anti-persistance → MR.
/// Implémentation complète par DFA (Detrended Fluctuation Analysis) au Sprint suivant.
/// </summary>
public sealed class HurstEvidence : IRegimeEvidence
{
    public string ModelName => "Hurst";

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

        // Variance des rendements à 1 pas
        int n1 = _n - 1;
        decimal sum1 = 0m, sum1Sq = 0m;
        for (int i = 0; i < n1; i++) { decimal r = P(i) - P(i + 1); sum1 += r; sum1Sq += r * r; }
        decimal mu1 = sum1 / n1;
        decimal var1 = sum1Sq / n1 - mu1 * mu1;

        // Variance des rendements à 2 pas
        int n2 = _n - 2;
        decimal sum2 = 0m, sum2Sq = 0m;
        for (int i = 0; i < n2; i++) { decimal r = P(i) - P(i + 2); sum2 += r; sum2Sq += r * r; }
        decimal mu2 = sum2 / n2;
        decimal var2 = sum2Sq / n2 - mu2 * mu2;

        if (var1 <= 0m) return Stub();

        var vr    = var2 / (2m * var1);
        var dev   = vr - 1m;
        var dir   = dev > 0.10m ? 1 : dev < -0.10m ? -1 : 0;
        var score = Math.Clamp(Math.Abs(dev) / 0.5m, 0m, 1m);
        var hint  = ResolveHint(dir, P(0), P(_n - 1));

        return new EvidenceResult
        {
            ModelName   = ModelName,
            Score       = score,
            Confidence  = Math.Clamp((decimal)_n / W, 0m, 1m),
            Direction   = dir,
            RegimeHint  = hint,
            Explanation = $"VR(2)={vr:F3}",
            IsReady     = true
        };
    }

    private decimal P(int lag) => _buf[(_h - 1 - lag + W) % W];

    private static RegimeType ResolveHint(int dir, decimal latest, decimal oldest) =>
        dir > 0 ? (latest >= oldest ? RegimeType.TrendBull : RegimeType.TrendBear)
      : dir < 0 ? RegimeType.MeanReversion
      : RegimeType.Transition;

    private EvidenceResult Stub() => new()
    {
        ModelName = ModelName, Score = 0m, Confidence = 0m,
        Direction = 0, RegimeHint = RegimeType.Unknown,
        Explanation = "Warmup", IsReady = false
    };
}
