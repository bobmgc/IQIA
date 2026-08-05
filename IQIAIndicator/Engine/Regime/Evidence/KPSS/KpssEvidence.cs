using IQIAIndicator.Core;

// Namespace identique à l'ancien placeholder → RegimeEngine inchangé
namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Test KPSS complet (Kwiatkowski–Phillips–Schmidt–Shin, 1992).
///
/// H₀ : stationnarité (en niveau par défaut, regression='c').
/// H₁ : racine unitaire (non-stationnaire).
///
/// Algorithme :
///   1. Buffer glissant de W prix (oldest-first lors du calcul).
///   2. Résidus de démoyennage : ê_t = y_t − ȳ.
///   3. Sommes cumulées : S_t = Σ_{i=1}^{t} ê_i.
///   4. Variance de long terme Newey-West : ŝ² (noyau Bartlett, bande Schwert).
///   5. Statistique : η̂ = T⁻² · Σ S_t² / ŝ².
///   6. Valeurs critiques Kwiatkowski et al. (1992) Table 1.
///   7. P-value interpolée à 5 points.
///
/// Complémentarité avec l'ADF :
///   ADF   H₀ = racine unitaire → rejet = stationnaire
///   KPSS  H₀ = stationnaire    → rejet = racine unitaire
///
/// Convergence ADF+KPSS sur la même conclusion renforce la confiance.
/// </summary>
public sealed class KpssEvidence : IRegimeEvidence
{
    public string ModelName => "KPSS";

    // ── Paramètres ────────────────────────────────────────────────────────
    private const int W    = 60;   // fenêtre glissante
    private const int MinN = 30;   // minimum avant premier calcul

    // ── Buffer glissant ───────────────────────────────────────────────────
    private readonly decimal[] _buf      = new decimal[W];
    private int _h, _n;

    // ── Buffers de travail pré-alloués ────────────────────────────────────
    private readonly decimal[] _window    = new decimal[W];
    private readonly decimal[] _residuals = new decimal[W];

    // ── Point d'entrée IRegimeEvidence ───────────────────────────────────

    public EvidenceResult Compute(MarketContext ctx)
    {
        if (ctx.Clock.IsFirstBar) { _h = _n = 0; Array.Clear(_buf); }

        _buf[_h] = ctx.Price.Close;
        _h = (_h + 1) % W;
        _n = Math.Min(_n + 1, W);

        if (_n < MinN) return Warmup();

        var result = ComputeKpss();
        return ToEvidenceResult(result);
    }

    // ── Calcul KPSS ──────────────────────────────────────────────────────

    private KPSS.KpssResult ComputeKpss()
    {
        // Remplir _window oldest-first
        for (int i = 0; i < _n; i++)
            _window[i] = _buf[(_h - _n + i + W) % W];

        // Résidus : démoyennage (spécification 'c')
        if (!KPSS.KpssRegression.TryDemean(_window, _n, _residuals))
            return KPSS.KpssResult.Invalid("Démoyennage impossible (série constante ?).");

        // Variance de long terme (Newey-West)
        decimal lrv = KPSS.KpssLongRunVariance.Compute(_residuals, _n);
        if (lrv <= 0m)
            return KPSS.KpssResult.Invalid("Variance de long terme nulle.");

        // Statistique η̂
        decimal stat = KPSS.KpssStatistics.ComputeStatistic(_residuals, _n, lrv);
        if (stat < 0m)
            return KPSS.KpssResult.Invalid("Calcul de η̂ impossible.");

        // Valeurs critiques
        var (cv1, cv25, cv5, cv10) = KPSS.KpssCriticalValues.Get(withTrend: false);
        decimal pv = KPSS.KpssStatistics.ApproximatePValue(stat, cv1, cv25, cv5, cv10);
        bool stationary = stat < cv5;

        int bw = KPSS.KpssLongRunVariance.ComputeBandwidth(_n);

        string expl = stationary
            ? $"η̂={stat:F4}, p≈{pv:F4}, KPSS (l={bw}), Maintient H₀ @5% → Stationnaire"
            : $"η̂={stat:F4}, p≈{pv:F4}, KPSS (l={bw}), Rejette H₀ @5% → Racine unitaire";

        return new KPSS.KpssResult
        {
            Statistic       = stat,
            PValue          = pv,
            CriticalValue1  = cv1,
            CriticalValue5  = cv5,
            CriticalValue10 = cv10,
            IsStationary    = stationary,
            Bandwidth       = bw,
            SampleSize      = _n,
            Explanation     = expl,
            IsValid         = true
        };
    }

    // ── Projection KpssResult → EvidenceResult ───────────────────────────

    private EvidenceResult ToEvidenceResult(KPSS.KpssResult r)
    {
        if (!r.IsValid)
            return new EvidenceResult
            {
                ModelName   = ModelName, Score = 0m, Confidence = 0m,
                Direction   = 0, RegimeHint = RegimeType.Unknown,
                Explanation = r.Explanation, IsReady = true
            };

        // Score symétrique autour de p=0.50 (confiance dans le hint)
        decimal score = Math.Clamp(Math.Abs(0.5m - r.PValue) * 2m, 0m, 1m);

        // Direction : −1 stationnaire, +1 racine unitaire
        int direction = r.PValue > 0.5m ? -1 : 1;

        decimal confidence = Math.Clamp((decimal)_n / W, 0m, 1m);

        // Hint segmenté par p-value
        RegimeType hint;
        if (r.PValue > 0.70m)
            hint = RegimeType.MeanReversion;         // fortement stationnaire
        else if (r.PValue > 0.30m)
            hint = RegimeType.Range;                 // modérément stationnaire
        else if (r.PValue > 0.05m)
            hint = RegimeType.Transition;            // zone grise
        else
            hint = PriceDirection() > 0              // clairement non-stationnaire
                ? RegimeType.TrendBull
                : RegimeType.TrendBear;

        return new EvidenceResult
        {
            ModelName   = ModelName,
            Score       = score,
            Confidence  = confidence,
            Direction   = direction,
            RegimeHint  = hint,
            Explanation = r.Explanation,
            IsReady     = true
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private int PriceDirection()
    {
        decimal newest = _buf[(_h - 1 + W) % W];
        decimal oldest = _buf[(_h - _n + W) % W];
        return newest >= oldest ? 1 : -1;
    }

    private EvidenceResult Warmup() => new()
    {
        ModelName   = ModelName, Score = 0m, Confidence = 0m,
        Direction   = 0, RegimeHint = RegimeType.Unknown,
        Explanation = $"Warmup KPSS ({_n}/{MinN} bars).", IsReady = false
    };
}
