using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.ScientificFusion;

namespace IQIAIndicator.Engine.EntryTrigger;

public sealed class EntryTriggerBuilder
{
    // Sprint 15.25 (Lot 9 - QDE-012 offline OOS threshold study, Lots 4-8): candidate value from the
    // offline calibration/backtest/OOS-validation study (Difference > 0.05, i.e. AmbiguityScore < 0.95 -
    // see AmbiguityScore = Clamp(1 - Difference, 0, 1) in DecisionArbitrator.Arbitrate). Was 0.5
    // (Difference > 0.5) before this lot; that value produced zero BUY/SELL candidates on every real
    // ATAS capture analyzed (Lots 2-3 audit). Live ATAS validation with this value is still required
    // (see Lot 9 report) - change this single constant back to 0.5 to revert.
    //
    // Sprint 15.25 (Lot 14.10, P0-3, explicit exception to this file's protected status - see the Lot
    // 14.10 brief's "REGIME / FUSION / DECISION" section): this production default is UNCHANGED (still
    // 0.95, still the value every live/backtest caller gets unless it explicitly overrides it). What
    // changed is that the value is no longer a hard-coded literal read only from this file - it is now
    // also an explicit, typed constructor parameter (see the two constructors below), so a calibration
    // experiment can supply a different value FOR ITSELF, local to one BacktestEngine call, without ever
    // mutating this constant or any shared/static/global state. See
    // Backtest.Calibration.CalibrationParameterBinding for the one place a CalibrationParameterSet is
    // ever translated into this override.
    public const double AmbiguityGateThreshold = 0.95;

    private readonly double _ambiguityGateThreshold;
    private readonly double _minMomentumConfidence;

    /// <summary>Production default: identical behaviour to every EntryTriggerBuilder that existed before
    /// Lot 14.10 (brief's "RÈGLE DE NON-RÉGRESSION").</summary>
    public EntryTriggerBuilder() : this(AmbiguityGateThreshold, DefaultMinMomentumConfidence)
    {
    }

    /// <summary>Audit 2026-08-30 (P0-2): ambiguity-gate-only overload retained for existing callers;
    /// MinMomentumConfidence keeps its production default.</summary>
    public EntryTriggerBuilder(double ambiguityGateThreshold)
        : this(ambiguityGateThreshold, DefaultMinMomentumConfidence)
    {
    }

    /// <summary>Sprint 15.25 (Lot 14.10, P0-3): the sole injection point for a calibration experiment to
    /// exercise a different ambiguity gate than the production default, without touching the constant
    /// above or any other file. <paramref name="ambiguityGateThreshold"/> is not validated against [0, 1]
    /// here - DecisionArbitrator already guarantees AmbiguityScore itself is clamped to [0, 1], so any
    /// threshold outside that range is simply always/never satisfied, a legitimate (if degenerate)
    /// experiment configuration, never a crash. Audit 2026-08-30 (P0-2): <paramref name="minMomentumConfidence"/>
    /// is the trend-following equivalent - the floor a trending bar's TimeSeriesMomentumModel confidence
    /// must clear before a BUY/SELL is emitted (see DetermineDirection). Not range-validated for the same
    /// reason.</summary>
    public EntryTriggerBuilder(double ambiguityGateThreshold, double minMomentumConfidence)
    {
        _ambiguityGateThreshold = ambiguityGateThreshold;
        _minMomentumConfidence = minMomentumConfidence;
    }

    private readonly List<string> _warnings = new();
    private readonly List<string> _diagnostics = new();
    private EntryTriggerStatus _triggerStatus = EntryTriggerStatus.INVALID;
    private DirectionCandidate _direction = DirectionCandidate.NO_ACTION;
    private double _scientificConfidence;
    private double _opportunityPriority;
    private EntryTriggerReason _reason = EntryTriggerReason.UNKNOWN;
    private EntryTriggerReason? _noActionReason;
    private double? _estimatedEquilibrium;
    private double? _distanceToEquilibrium;
    private decimal _currentPrice;

    public void Populate(EntryTriggerContext context)
    {
        _warnings.Clear();
        _diagnostics.Clear();
        _triggerStatus = EntryTriggerStatus.INVALID;
        _direction = DirectionCandidate.NO_ACTION;
        _scientificConfidence = 0.0;
        _opportunityPriority = 0.0;
        _reason = EntryTriggerReason.UNKNOWN;
        _noActionReason = null;
        _estimatedEquilibrium = null;
        _distanceToEquilibrium = null;
        _currentPrice = 0m;

        if (context is null)
        {
            _diagnostics.Add("EntryTriggerContext is null.");
            _triggerStatus = EntryTriggerStatus.INVALID;
            _reason = EntryTriggerReason.INVALID_CONTEXT;
            return;
        }

        _scientificConfidence = context.ScientificAssessment.OverallConfidence;
        _opportunityPriority = context.EntryCandidate.OpportunityPriority;
        _currentPrice = context.BusinessContext.CurrentPrice;
        _estimatedEquilibrium = context.BusinessContext.EstimatedEquilibrium;
        _distanceToEquilibrium = context.BusinessContext.DistanceToEquilibrium;
        _direction = DetermineDirection(context, _ambiguityGateThreshold, _minMomentumConfidence, out string? directionSuppressionReason, out _noActionReason);
        _triggerStatus = DetermineTriggerStatus(context);
        _reason = BuildReason(context);

        if (directionSuppressionReason is not null)
        {
            _diagnostics.Add(directionSuppressionReason);
        }

        if (_currentPrice <= 0m)
        {
            _warnings.Add("Prix courant indisponible ou invalide.");
        }

        if (_estimatedEquilibrium is null)
        {
            _warnings.Add("Niveau d'équilibre estimé indisponible.");
        }

        if (context.EntryAssessment.BlockingIssues is { Count: > 0 })
        {
            _warnings.AddRange(context.EntryAssessment.BlockingIssues.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        if (context.EntryAssessment.Diagnostics is not null && context.EntryAssessment.Diagnostics.Count > 0)
        {
            _diagnostics.AddRange(context.EntryAssessment.Diagnostics.Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic)));
        }
    }

    public EntryTriggerCandidate Build(EntryTriggerContext context)
    {
        Populate(context);

        var assessment = new EntryTriggerAssessment(
            _triggerStatus,
            _direction,
            _scientificConfidence,
            _opportunityPriority,
            _reason,
            _estimatedEquilibrium,
            _distanceToEquilibrium,
            DateTime.UtcNow);

        return new EntryTriggerCandidate(
            assessment,
            context.EntryCandidate,
            _currentPrice,
            _warnings.AsReadOnly(),
            _diagnostics.AsReadOnly(),
            DateTime.UtcNow);
    }

    // Business data (current price, estimated equilibrium, dynamic z-score, distance, etc.)
    // are provided via EntryBusinessContext. EntryTrigger must not recompute scientific metrics.

    /// <summary>
    /// Sprint 15.5 (C1): Direction must be coherent with the regime the DecisionEngine actually
    /// arbitrated, not computed independently of it (Sprint 15.4 audit finding BC-03 - previously this
    /// method read only the raw DynamicZScore sign, with no access to DecisionResult at all). Per the
    /// ScientificModelRegistry audit (C2, see ScientificModelRegistry.Resolve's own doc comment), the
    /// ONLY methodology backed by real, non-placeholder scientific models capable of a directional read
    /// is MeanReversionMethodology (DynamicZScoreModel, fed by KalmanFilterModel). No other regime
    /// currently has a model that can support a reliable BUY/SELL call - producing one anyway would be
    /// exactly the fabricated signal this sprint is required to avoid, so every other regime (and any
    /// case where the decision itself cannot be verified) returns NO_ACTION regardless of any raw
    /// metric. This does not add a new directional rule - the sign-of-DynamicZScore logic is unchanged
    /// for the one regime it was already valid for; it only gates that existing logic behind proof that
    /// the regime it depends on was actually the one arbitrated.
    ///
    /// Sprint 15.25 (Lot 15.1, brief §11/§12/§30): the regime-mismatch branch below now also records
    /// <see cref="EntryTriggerReason.UNSUPPORTED_REGIME"/> (previously left <c>noActionReason</c> null,
    /// so this branch's cause was never distinguishable from any other NO_ACTION downstream - Lot 15.0
    /// audit finding). The GATING CONDITION itself is UNCHANGED (still exactly
    /// <c>decision.Winner != MarketState.MeanReverting</c> - Lot 15.0 confirmed this is bit-for-bit
    /// equivalent, for every regime reachable in production, to gating on
    /// <see cref="Engine.ScientificFusion.ScientificCoverageStatus.NoModelCoverage"/>, since
    /// MethodologyEngine.Evaluate -> MethodologyRegistry.Resolve -> ScientificModelRegistry.Resolve is a
    /// deterministic, unconditional chain keyed only on decisionResult.Winner - but switching the
    /// condition itself to read CoverageStatus was rejected: several existing hand-built
    /// EntryTriggerContext test fixtures across the suite construct ScientificAssessment without setting
    /// CoverageStatus explicitly, relying on ScientificAssessment's declared default
    /// (NoModelCoverage) independently of the MarketState they pass to Trigger(...) - switching the
    /// condition would have silently broken every one of those MeanReverting-winner fixtures for a
    /// purely cosmetic robustness gain this lot's brief does not require (brief §15: "ne pas créer une
    /// nouvelle abstraction... si le repository possède déjà le mécanisme approprié" cuts both ways -
    /// reuse what exists, but do not rewire it beyond what the fix needs). CoverageStatus is still
    /// surfaced, read-only, in the diagnostic message below - the existing distinction (brief §13) is
    /// reused for OBSERVABILITY, not made load-bearing for behaviour. This is NOT the "IRegimeSignalModel
    /// abstraction" contemplated by brief §15/§16 - no such abstraction was created; none of the
    /// repository's existing mechanisms needed one to satisfy this lot's acceptance criteria.
    /// No fallback strategy is substituted for any regime (brief's "RÈGLE ABSOLUE") - the outcome for
    /// every non-MeanReverting regime remains exactly DirectionCandidate.NO_ACTION, unchanged; only the
    /// recorded Reason becomes explicit.
    /// </summary>
    private static DirectionCandidate DetermineDirection(EntryTriggerContext context, double ambiguityGateThreshold, double minMomentumConfidence, out string? suppressionReason, out EntryTriggerReason? noActionReason)
    {
        suppressionReason = null;
        noActionReason = null;

        if (context.EntryCandidate.OpportunityStatus == OpportunityStatus.WATCHLIST)
        {
            return DirectionCandidate.WATCH;
        }

        DecisionResult? decision = context.MethodologySelection?.DecisionResult;
        if (decision is null)
        {
            suppressionReason = "Direction suppressed: no DecisionResult available to verify coherence with the arbitrated regime.";
            return DirectionCandidate.NO_ACTION;
        }

        // The ambiguity gate is described in detail below; it applies identically to every regime that
        // HAS a directional model (MeanReverting, and - audit 2026-08-30, P0-2 - Trending). An
        // ambiguous arbitration (winner barely ahead of the runner-up - see DecisionArbitrator
        // .Arbitrate, AmbiguityScore = Clamp(1 - (winnerScore - runnerUpScore), 0, 1)) means the
        // regime call itself isn't reliably established. Trading a direction derived from a
        // regime-specific model when the regime call is close to a coin flip would fabricate
        // confidence the arbitration doesn't actually have. Sprint 15.25 (Lot 9): threshold moved from
        // the original midpoint (0.5) to AmbiguityGateThreshold (0.95, equivalent to Difference >
        // 0.05) - the original 0.5 value was never once satisfied on any real ATAS capture analyzed
        // (Lots 2-3), permanently blocking Direction; 0.95 is the candidate value from the Lots 4-8
        // offline OOS study. See AmbiguityGateThreshold's own doc comment for the reversion path and
        // the Lot 9 report for the live-ATAS validation this change still requires.
        bool ambiguous = decision.AmbiguityScore >= ambiguityGateThreshold;

        if (decision.Winner == MarketState.MeanReverting)
        {
            if (ambiguous)
            {
                suppressionReason = $"Direction suppressed: decision ambiguity {decision.AmbiguityScore:F3} >= {ambiguityGateThreshold:F2} (regime arbitration not decisive enough to trust a directional call).";
                noActionReason = EntryTriggerReason.DECISION_AMBIGUOUS;
                return DirectionCandidate.NO_ACTION;
            }

            if (TryGetDynamicZScore(context, out double zScore))
            {
                if (zScore > 0.0)
                {
                    return DirectionCandidate.SELL_CANDIDATE;
                }

                if (zScore < 0.0)
                {
                    return DirectionCandidate.BUY_CANDIDATE;
                }

                // Sprint 15.7.1: zScore == 0 is the price sitting exactly at the estimated equilibrium -
                // no mean-reversion deviation to trade. This is not a new trading rule; the sign-based
                // BUY/SELL logic above is unchanged, this only names the case it already fell through to.
                suppressionReason = "Direction suppressed: price is at the estimated equilibrium (DynamicZScore == 0); no directional mean-reversion deviation is present.";
                noActionReason = EntryTriggerReason.PRICE_AT_EQUILIBRIUM;
                return DirectionCandidate.NO_ACTION;
            }

            suppressionReason = "Direction suppressed: DynamicZScore unavailable (not present in business context or scientific results).";
            noActionReason = EntryTriggerReason.DYNAMIC_ZSCORE_UNAVAILABLE;
            return DirectionCandidate.NO_ACTION;
        }

        // Audit 2026-08-30 (P0-2). Trend-following: the direction is momentum CONTINUATION (positive
        // multi-horizon momentum -> BUY), the opposite of the mean-reversion fade above. Read straight
        // from TimeSeriesMomentumModel's result - never recomputed here (EntryTrigger must not compute
        // scientific metrics).
        if (decision.Winner == MarketState.Trending)
        {
            if (ambiguous)
            {
                suppressionReason = $"Direction suppressed: decision ambiguity {decision.AmbiguityScore:F3} >= {ambiguityGateThreshold:F2} (regime arbitration not decisive enough to trust a directional call).";
                noActionReason = EntryTriggerReason.DECISION_AMBIGUOUS;
                return DirectionCandidate.NO_ACTION;
            }

            if (TryGetMomentum(context, out double momentumScore, out double momentumConfidence))
            {
                if (momentumConfidence < minMomentumConfidence || momentumScore == 0.0)
                {
                    suppressionReason = $"Direction suppressed: momentum too weak/undirected (MomentumScore={momentumScore:F3}, MomentumConfidence={momentumConfidence:F3} < {minMomentumConfidence:F2}).";
                    noActionReason = EntryTriggerReason.INSUFFICIENT_MOMENTUM;
                    return DirectionCandidate.NO_ACTION;
                }

                return momentumScore > 0.0 ? DirectionCandidate.BUY_CANDIDATE : DirectionCandidate.SELL_CANDIDATE;
            }

            suppressionReason = "Direction suppressed: MomentumScore unavailable (not present in the scientific results).";
            noActionReason = EntryTriggerReason.MOMENTUM_UNAVAILABLE;
            return DirectionCandidate.NO_ACTION;
        }

        suppressionReason = $"Direction suppressed: regime={decision.Winner} has no scientific model capable of a directional read (ScientificAssessment.CoverageStatus={context.ScientificAssessment.CoverageStatus}; only MeanReverting and Trending are currently supported - see MethodologyRegistry/ScientificModelRegistry).";
        noActionReason = EntryTriggerReason.UNSUPPORTED_REGIME;
        return DirectionCandidate.NO_ACTION;
    }

    /// <summary>NON CALIBRATED (audit 2026-08-30, P0-2). Minimum TimeSeriesMomentumModel confidence
    /// (|MomentumScore| x HorizonAgreement x PersistenceFactor) before a trending BUY/SELL is emitted -
    /// a weak/undirected trend produces NO_ACTION (INSUFFICIENT_MOMENTUM), never a coin-flip trade.</summary>
    /// <summary>NON CALIBRATED production default (audit 2026-08-30, P0-2). Overridable per backtest
    /// call via PipelineParameterOverrides.MinMomentumConfidence.</summary>
    public const double DefaultMinMomentumConfidence = 0.10;

    private static bool TryGetMomentum(EntryTriggerContext context, out double momentumScore, out double momentumConfidence)
    {
        momentumScore = 0.0;
        momentumConfidence = 0.0;

        IReadOnlyList<Engine.ScientificModels.Abstractions.ScientificModelResult>? results =
            context.ScientificAssessment?.ScientificResults;
        if (results is null)
        {
            return false;
        }

        foreach (Engine.ScientificModels.Abstractions.ScientificModelResult result in results)
        {
            if (!string.Equals(result.ModelName, "TimeSeriesMomentumModel", StringComparison.OrdinalIgnoreCase) ||
                !result.Success || result.Metrics is null)
            {
                continue;
            }

            if (result.Metrics.TryGetValue("MomentumScore", out object? rawScore) && rawScore is double score && double.IsFinite(score))
            {
                momentumScore = score;
                momentumConfidence = result.Metrics.TryGetValue("MomentumConfidence", out object? rawConf) && rawConf is double conf && double.IsFinite(conf)
                    ? conf
                    : result.Score;
                return true;
            }
        }

        return false;
    }
    private static bool TryGetDynamicZScore(EntryTriggerContext context, out double zScore)
    {
        zScore = 0.0;

        if (context.BusinessContext.DynamicZScore is double dz && double.IsFinite(dz))
        {
            zScore = dz;
            return true;
        }

        // Fallback: inspect scientific results if business context did not contain the metric.
        if (context.ScientificAssessment?.ScientificResults is null)
        {
            return false;
        }

        foreach (var result in context.ScientificAssessment.ScientificResults)
        {
            if (!string.Equals(result.ModelName, "DynamicZScoreModel", StringComparison.OrdinalIgnoreCase) || result.Metrics is null)
            {
                continue;
            }

            if (result.Metrics.TryGetValue("DynamicZScore", out var raw) && raw is double typed && double.IsFinite(typed))
            {
                zScore = typed;
                return true;
            }
        }

        return false;
    }

    private EntryTriggerStatus DetermineTriggerStatus(EntryTriggerContext context)
    {
        if (context.EntryCandidate.OpportunityStatus == OpportunityStatus.NOT_QUALIFIED)
        {
            return context.EntryAssessment.BlockingIssues is { Count: > 0 }
                ? EntryTriggerStatus.EXPIRED
                : EntryTriggerStatus.NOT_READY;
        }

        if (context.EntryCandidate.OpportunityStatus == OpportunityStatus.WATCHLIST)
        {
            return EntryTriggerStatus.WATCH;
        }

        if (context.EntryCandidate.OpportunityStatus == OpportunityStatus.QUALIFIED)
        {
            return _scientificConfidence >= 0.70
                ? EntryTriggerStatus.READY
                : EntryTriggerStatus.WATCH;
        }

        if (context.EntryCandidate.OpportunityStatus == OpportunityStatus.HIGH_PRIORITY)
        {
            return _scientificConfidence >= 0.55
                ? EntryTriggerStatus.READY
                : EntryTriggerStatus.WATCH;
        }

        return EntryTriggerStatus.NOT_READY;
    }

    private EntryTriggerReason BuildReason(EntryTriggerContext context)
    {
        // Sprint 15.7.1: when TriggerStatus is READY but Direction resolved to NO_ACTION, surface the
        // specific reason DetermineDirection already computed instead of the generic READY reason -
        // this does not change TriggerStatus, Direction, or any trading condition, only which
        // EntryTriggerReason value is reported for a case that already existed.
        //
        // Sprint 15.25 (Lot 15.1, brief §12/§14): UNSUPPORTED_REGIME is surfaced regardless of
        // TriggerStatus - unlike the three Sprint 15.7.1 reasons above (which only ever apply to
        // MeanReverting, where OpportunityStatus is typically QUALIFIED/HIGH_PRIORITY and TriggerStatus
        // is therefore READY), a regime with zero registered scientific models has
        // OpportunityStatus=NOT_QUALIFIED upstream (EntryAssessmentBuilder.Build, driven by
        // ScientificAssessment.MissingEvidence being non-empty), so TriggerStatus is EXPIRED/NOT_READY
        // here, never READY - confirmed on the Lot 15.0 dataset. Without this widened condition,
        // UNSUPPORTED_REGIME would be computed by DetermineDirection but never actually reach
        // EntryTriggerAssessment.Reason, silently falling back to the generic BLOCKED/NOT_READY value
        // this lot exists to make explicit (brief §12: "Un régime sans modèle scientifique doit
        // produire NO SIGNAL / UNSUPPORTED et non... une absence explicite de signal").
        if (_direction == DirectionCandidate.NO_ACTION && _noActionReason.HasValue &&
            (_triggerStatus == EntryTriggerStatus.READY || _noActionReason.Value == EntryTriggerReason.UNSUPPORTED_REGIME))
        {
            return _noActionReason.Value;
        }

        return _triggerStatus switch
        {
            EntryTriggerStatus.INVALID => EntryTriggerReason.INVALID_CONTEXT,
            EntryTriggerStatus.EXPIRED => EntryTriggerReason.BLOCKED,
            EntryTriggerStatus.NOT_READY => EntryTriggerReason.NOT_READY,
            EntryTriggerStatus.WATCH => EntryTriggerReason.WATCH,
            EntryTriggerStatus.READY => EntryTriggerReason.READY,
            _ => EntryTriggerReason.UNKNOWN
        };
    }
}
