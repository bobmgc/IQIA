using IQIAIndicator.Core;

// Namespace identique à l'ancien placeholder → RegimeEngine inchangé
namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Test ADF complet (Augmented Dickey-Fuller).
///
/// Algorithme :
///   1. Buffer glissant de W prix (oldest-first lors du calcul).
///   2. Sélection du lag p optimal par critère AIC (méthode Schwert bornée).
///   3. Régression MCO : Δy_t = α + β·y_{t-1} + Σγⱼ·Δy_{t-j} + ε.
///   4. Statistique τ = β̂ / SE(β̂).
///   5. Valeurs critiques MacKinnon (1994) pour T effectif.
///   6. P-value interpolée (surface de réponse exacte au sprint suivant).
///
/// H₀ : racine unitaire (non-stationnaire / tendance).
/// H₁ : stationnarité (retour à la moyenne).
/// </summary>
public sealed class AdfEvidence : IRegimeEvidence
{
    public string ModelName => "ADF";

    // ── Paramètres ────────────────────────────────────────────────────────
    private const int W    = 60;   // taille de la fenêtre glissante
    private const int MinN = 30;   // minimum de bars avant le premier calcul

    // ── Buffer glissant ───────────────────────────────────────────────────
    private readonly decimal[] _buf = new decimal[W];
    private int _h, _n;            // _h = prochain index d'écriture, _n = count

    // ── Buffer de travail (pré-alloué, réutilisé à chaque bar) ───────────
    private readonly decimal[] _window = new decimal[W];

    // ── Point d'entrée IRegimeEvidence ───────────────────────────────────

    public EvidenceResult Compute(MarketContext ctx)
    {
        if (ctx.Clock.IsFirstBar) { _h = _n = 0; Array.Clear(_buf); }

        _buf[_h] = ctx.Price.Close;
        _h = (_h + 1) % W;
        _n = Math.Min(_n + 1, W);

        if (_n < MinN)
            return Warmup();

        var result = ComputeAdf();
        return ToEvidenceResult(result);
    }

    // ── Calcul ADF ────────────────────────────────────────────────────────

    private ADF.AdfResult ComputeAdf()
    {
        // Remplir _window oldest-first : _window[0] = plus ancien
        for (int i = 0; i < _n; i++)
            _window[i] = _buf[(_h - _n + i + W) % W];

        // Sélection du lag optimal (AIC)
        int lag = ADF.AdfStatistics.SelectLag(_window, _n);

        if (!ADF.AdfRegression.TryCompute(_window, _n, lag,
                out decimal tStat, out _, out int nObs))
            return ADF.AdfResult.Invalid($"Régression ADF échouée (T={_n}, p={lag}).");

        var (cv1, cv5, cv10) = ADF.AdfCriticalValues.Get(nObs);
        decimal pv = ADF.AdfStatistics.ApproximatePValue(tStat, cv1, cv5, cv10);
        bool stationary = tStat < cv5;

        string expl = stationary
            ? $"τ={tStat:F4}, p≈{pv:F4}, ADF({lag}), Rejette H₀ @5% → Stationnaire"
            : $"τ={tStat:F4}, p≈{pv:F4}, ADF({lag}), Maintient H₀ @5% → Racine unitaire";

        return new ADF.AdfResult
        {
            Statistic       = tStat,
            PValue          = pv,
            CriticalValue1  = cv1,
            CriticalValue5  = cv5,
            CriticalValue10 = cv10,
            IsStationary    = stationary,
            LagUsed         = lag,
            SampleSize      = nObs,
            Explanation     = expl,
            IsValid         = true
        };
    }

    // ── Projection AdfResult → EvidenceResult ────────────────────────────

    private EvidenceResult ToEvidenceResult(ADF.AdfResult r)
    {
        if (!r.IsValid)
            return new EvidenceResult
            {
                ModelName   = ModelName, Score = 0m, Confidence = 0m,
                Direction   = 0, RegimeHint = RegimeType.Unknown,
                Explanation = r.Explanation, IsReady = true
            };

        // Score = distance symétrique par rapport à p=0.50
        // → 1 quand la série est clairement stationnaire ou clairement non-stationnaire
        // → 0 quand p=0.50 (aucune évidence)
        decimal score = Math.Clamp(Math.Abs(0.5m - r.PValue) * 2m, 0m, 1m);

        // Direction : −1 → stationnaire (retour à la moyenne)
        //             +1 → racine unitaire (tendance)
        int direction = r.PValue < 0.5m ? -1 : 1;

        // Confiance proportionnelle à la taille de l'échantillon
        decimal confidence = Math.Clamp((decimal)_n / W, 0m, 1m);

        // Hint régime : segmenté par p-value
        RegimeType hint;
        if (r.PValue < 0.05m)
            hint = RegimeType.MeanReversion;
        else if (r.PValue < 0.30m)
            hint = RegimeType.Range;
        else if (r.PValue < 0.70m)
            hint = RegimeType.Transition;
        else
            hint = PriceDirection() > 0 ? RegimeType.TrendBull : RegimeType.TrendBear;

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
        Explanation = $"Warmup ADF ({_n}/{MinN} bars).", IsReady = false
    };
}
