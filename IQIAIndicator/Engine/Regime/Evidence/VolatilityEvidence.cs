using IQIAIndicator.Core;

namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Clustering de volatilité : autocorrélation au lag 1 des rendements absolus.
/// ACF(|r|) élevée → la volatilité haute suit la volatilité haute → Expansion.
/// ACF(|r|) faible ou négative → pas de clustering → Compression.
/// Proxy de l'effet ARCH ; modèle GARCH complet au Sprint suivant.
/// </summary>
public sealed class VolatilityEvidence : IRegimeEvidence
{
    public string ModelName => "Volatility";

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

        // Moyenne des |rendements|
        decimal sumA = 0m;
        for (int i = 0; i < n; i++) sumA += Math.Abs(P(i) - P(i + 1));
        decimal muA = sumA / n;

        // Autocorrélation au lag 1 des |rendements|
        decimal cov = 0m, varA = 0m;
        for (int i = 0; i < n - 1; i++)
        {
            decimal a0 = Math.Abs(P(i)     - P(i + 1)) - muA;
            decimal a1 = Math.Abs(P(i + 1) - P(i + 2)) - muA;
            cov  += a0 * a1;
            varA += a0 * a0;
        }

        if (varA == 0m) return Stub();

        var acf   = cov / varA;
        var score = Math.Clamp(Math.Abs(acf), 0m, 1m);
        var dir   = acf > 0.05m ? 1 : acf < -0.05m ? -1 : 0;
        var hint  = dir > 0 ? RegimeType.Expansion
                  : dir < 0 ? RegimeType.Compression
                  : RegimeType.Transition;

        return new EvidenceResult
        {
            ModelName   = ModelName,
            Score       = score,
            Confidence  = Math.Clamp((decimal)_n / W, 0m, 1m),
            Direction   = dir,
            RegimeHint  = hint,
            Explanation = $"ACF(|r|)={acf:F3}",
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
