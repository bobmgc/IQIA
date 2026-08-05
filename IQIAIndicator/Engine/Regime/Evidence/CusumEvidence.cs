using IQIAIndicator.Core;

namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// CUSUM (Cumulative Sum) : détecte un déplacement persistant de la moyenne des rendements.
/// S+ élevé → dérive haussière persistante → TrendBull.
/// S- élevé → dérive baissière persistante → TrendBear.
/// S+ et S- faibles → pas de rupture → Range ou Transition.
/// </summary>
public sealed class CusumEvidence : IRegimeEvidence
{
    public string ModelName => "CUSUM";

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

        int n = _n - 1;

        // Moyenne et écart-type des rendements
        decimal sumR = 0m, sumR2 = 0m;
        for (int i = 0; i < n; i++) { decimal r = P(i) - P(i + 1); sumR += r; sumR2 += r * r; }
        decimal muR    = sumR / n;
        decimal sigma2 = sumR2 / n - muR * muR;
        decimal sigma  = sigma2 > 0m ? (decimal)Math.Sqrt((double)sigma2) : 0m;

        if (sigma <= 0m) return Stub();

        // CUSUM sur rendements standardisés (oldest → newest)
        decimal sPlus = 0m, sMinus = 0m;
        for (int j = 0; j < n; j++)
        {
            decimal z = (P(n - 1 - j) - P(n - j) - muR) / sigma;
            sPlus  = Math.Max(0m, sPlus  + z);
            sMinus = Math.Max(0m, sMinus - z);
        }

        // Seuil h = 4 σ standardisées (≈ 4 en z-score normalisé)
        const decimal h = 4m;
        var maxCusum = Math.Max(sPlus, sMinus);
        var score    = Math.Clamp(maxCusum / (h * 2m), 0m, 1m);
        var dir      = sPlus > sMinus ? 1 : sMinus > sPlus ? -1 : 0;

        var hint = maxCusum > h
            ? (dir > 0 ? RegimeType.TrendBull : RegimeType.TrendBear)
            : RegimeType.Transition;

        return new EvidenceResult
        {
            ModelName   = ModelName,
            Score       = score,
            Confidence  = Math.Clamp((decimal)_n / W, 0m, 1m),
            Direction   = dir,
            RegimeHint  = hint,
            Explanation = $"S+={sPlus:F2} S-={sMinus:F2}",
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
