using IQIAIndicator.Core;

namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Detection de rupture structurelle par CUSUM.
/// Producteur d'evidence pure : retourne CusumResult.
/// S+ et S- mesurent une derive persistante des rendements standardises.
/// </summary>
public sealed class CusumEvidence
{
    private const int W    = 30;
    private const int MinN = 20;
    private readonly decimal[] _buf = new decimal[W];
    private int _h, _n;

    public CusumResult Compute(MarketContext ctx)
    {
        if (ctx.Clock.IsFirstBar) { _h = _n = 0; Array.Clear(_buf); }
        _buf[_h] = ctx.Price.Close;
        _h = (_h + 1) % W;
        _n = Math.Min(_n + 1, W);
        if (_n < MinN) return CusumResult.Invalid($"Warmup CUSUM ({_n}/{MinN} bars).");

        int n = _n - 1;
        decimal sumR = 0m, sumR2 = 0m;
        for (int i = 0; i < n; i++) { var r = P(i) - P(i + 1); sumR += r; sumR2 += r * r; }
        decimal mu    = sumR / n;
        decimal sig2  = sumR2 / n - mu * mu;
        decimal sigma = sig2 > 0m ? (decimal)Math.Sqrt((double)sig2) : 0m;
        if (sigma <= 0m) return CusumResult.Invalid("Ecart-type nul.");

        decimal sPlus = 0m, sMinus = 0m;
        for (int j = 0; j < n; j++)
        {
            decimal z = (P(n - 1 - j) - P(n - j) - mu) / sigma;
            sPlus  = Math.Max(0m, sPlus  + z);
            sMinus = Math.Max(0m, sMinus - z);
        }

        const decimal h = 4m;
        bool hasBreak = Math.Max(sPlus, sMinus) > h;

        return new CusumResult
        {
            SPlus       = sPlus,
            SMinus      = sMinus,
            HasBreak    = hasBreak,
            Confidence  = Math.Clamp((decimal)_n / W, 0m, 1m),
            IsValid     = true,
            Explanation = $"S+={sPlus:F2}, S-={sMinus:F2}, rupture={hasBreak}"
        };
    }

    private decimal P(int lag) => _buf[(_h - 1 - lag + W) % W];
}
