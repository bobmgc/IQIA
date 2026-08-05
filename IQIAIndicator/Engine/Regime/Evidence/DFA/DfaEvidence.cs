using IQIAIndicator.Core;
using IQIAIndicator.Engine.Regime.Evidence.DFA;

// Namespace identique aux autres modèles — RegimeEngine inchangé
namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// DFA-1 (Detrended Fluctuation Analysis, ordre 1) appliqué aux log-rendements.
///
/// Pipeline :
///   1. Buffer glissant de W prix (oldest-first pour le calcul).
///   2. Log-rendements : r_t = ln(P_t / P_{t-1}).
///   3. Profil intégré : Y(k) = Σ_{i=1}^k (r_i − r̄).
///   4. Tailles de fenêtres logarithmiquement espacées.
///   5. F(n) = sqrt( mean_v [ (1/n) Σ résidus² ] ) — passes avant et arrière.
///   6. Régression log(F) ~ log(n) → exposant de Hurst H.
///
/// Producteur d'évidence pure : retourne DfaResult sans aucune décision de régime.
/// </summary>
public sealed class DfaEvidence
{
    // ── Paramètres ────────────────────────────────────────────────────────────
    private const int W    = 128;  // fenêtre glissante (assez grande pour DFA fiable)
    private const int MinN = 80;   // minimum avant premier calcul

    // ── Buffers (pré-alloués, sans allocation dans la boucle de calcul) ───────
    private readonly double[] _priceBuf   = new double[W];
    private readonly double[] _window     = new double[W];
    private readonly double[] _returns    = new double[W - 1];
    private readonly double[] _profile    = new double[W - 1];
    private readonly int[]    _validSizes  = new int[16];
    private readonly double[] _validFlucts = new double[16];
    private int _h, _n;

    // ── Point d'entrée ────────────────────────────────────────────────────────

    public DfaResult Compute(MarketContext ctx)
    {
        if (ctx.Clock.IsFirstBar) { _h = _n = 0; Array.Clear(_priceBuf); }

        _priceBuf[_h] = (double)ctx.Price.Close;
        _h = (_h + 1) % W;
        _n = Math.Min(_n + 1, W);

        if (_n < MinN)
            return DfaResult.Invalid($"Warmup DFA ({_n}/{MinN} bars).");

        return ComputeDfa();
    }

    // ── Pipeline DFA ──────────────────────────────────────────────────────────

    private DfaResult ComputeDfa()
    {
        // 1. Fenêtre oldest-first
        for (int i = 0; i < _n; i++)
            _window[i] = _priceBuf[(_h - _n + i + W) % W];

        // 2. Log-rendements
        int nR = _n - 1;
        if (!DfaMath.TryLogReturns(_window, _n, _returns))
            return DfaResult.Invalid("Prix invalides (≤ 0).");

        // 3. Profil intégré des rendements démoyennés
        double meanR = DfaMath.Mean(_returns, nR);
        DfaMath.IntegrateProfile(_returns, nR, meanR, _profile);

        // 4. Tailles de fenêtres
        int[] sizes = DfaStatistics.GenerateWindowSizes(nR);
        if (sizes.Length < 4)
            return DfaResult.Invalid($"Série trop courte pour le DFA (T={nR}).");

        // 5. F(n) pour chaque taille (filtre les NaN et négatifs)
        int valid = 0;
        for (int i = 0; i < sizes.Length && valid < 16; i++)
        {
            double f = DfaStatistics.ComputeFluctuation(_profile, nR, sizes[i]);
            if (double.IsNaN(f) || f <= 0.0) continue;
            _validSizes[valid]  = sizes[i];
            _validFlucts[valid] = f;
            valid++;
        }

        if (valid < 4)
            return DfaResult.Invalid("Trop peu de fenêtres valides après filtrage.");

        // 6. Régression log-log → H
        if (!DfaStatistics.EstimateHurst(_validSizes, _validFlucts, valid,
                out double hurst, out double r2))
            return DfaResult.Invalid("Régression log-log échouée.");

        hurst = Math.Clamp(hurst, 0.0, 2.0);

        // 7. Confiance : R² × couverture des fenêtres × taille d'échantillon
        double confidence = r2
            * Math.Min(1.0, valid / 6.0)
            * Math.Min(1.0, _n / (double)W);

        // 8. Snapshots pour DfaResult (allocation unique hors boucle chaude)
        var winArr   = new double[valid];
        var fluctArr = new double[valid];
        for (int i = 0; i < valid; i++)
        {
            winArr[i]   = _validSizes[i];
            fluctArr[i] = _validFlucts[i];
        }

        return new DfaResult
        {
            Hurst        = hurst,
            RSquared     = r2,
            Confidence   = confidence,
            WindowCount  = valid,
            IsValid      = true,
            WindowSizes  = winArr,
            Fluctuations = fluctArr,
            Explanation  = $"H={hurst:F4}, R²={r2:F3}, {valid} fenêtres, T={nR}"
        };
    }
}
