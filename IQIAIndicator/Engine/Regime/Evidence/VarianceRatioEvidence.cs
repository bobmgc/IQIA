using IQIAIndicator.Core;

namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Test de Lo-MacKinlay (Variance Ratio) à lag q=5.
/// VR(5) = Var(5-step returns) / (5 × Var(1-step returns)).
/// VR > 1 → autocorrélation positive → tendance. VR &lt; 1 → autocorrélation négative → MR.
/// Ce test est orthogonal à HurstEvidence (q=2) et apporte une évidence indépendante.
/// Implémentation complète avec correction hétéroscédastique (Z*) au Sprint suivant.
/// </summary>
public sealed class VarianceRatioEvidence : IRegimeEvidence
{
    public string ModelName => "VarianceRatio";

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
        decimal s1  = sum1Sq / n1 - mu1 * mu1;

        // Variance des rendements à 5 pas
        int n5 = _n - 5;
        if (n5 < 5) return Stub();
        decimal sum5 = 0m, sum5Sq = 0m;
        for (int i = 0; i < n5; i++) { decimal r = P(i) - P(i + 5); sum5 += r; sum5Sq += r * r; }
        decimal mu5 = sum5 / n5;
        decimal s5  = sum5Sq / n5 - mu5 * mu5;

        if (s1 <= 0m) return Stub();

        var vr    = s5 / (5m * s1);
        var dev   = vr - 1m;
        var dir   = dev > 0.10m ? 1 : dev < -0.10m ? -1 : 0;
        var score = Math.Clamp(Math.Abs(dev) / 0.6m, 0m, 1m);
        var hint  = ResolveHint(dir, P(0), P(_n - 1));

        return new EvidenceResult
        {
            ModelName   = ModelName,
            Score       = score,
            Confidence  = Math.Clamp((decimal)_n / W, 0m, 1m),
            Direction   = dir,
            RegimeHint  = hint,
            Explanation = $"VR(5)={vr:F3}",
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
