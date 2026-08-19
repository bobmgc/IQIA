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
    private const double AmbiguityGateThreshold = 0.95;

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
        _direction = DetermineDirection(context, out string? directionSuppressionReason, out _noActionReason);
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
    /// </summary>
    private static DirectionCandidate DetermineDirection(EntryTriggerContext context, out string? suppressionReason, out EntryTriggerReason? noActionReason)
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

        if (decision.Winner != MarketState.MeanReverting)
        {
            suppressionReason = $"Direction suppressed: regime={decision.Winner} has no scientific model capable of a directional read (only MeanReverting is currently supported).";
            return DirectionCandidate.NO_ACTION;
        }

        // An ambiguous arbitration (winner barely ahead of the runner-up - see
        // DecisionArbitrator.Arbitrate, AmbiguityScore = Clamp(1 - (winnerScore - runnerUpScore), 0, 1))
        // means the regime call itself isn't reliably established. Trading a direction derived from a
        // regime-specific model when the regime call is close to a coin flip would fabricate confidence
        // the arbitration doesn't actually have. Sprint 15.25 (Lot 9): threshold moved from the original
        // midpoint (0.5) to AmbiguityGateThreshold (0.95, equivalent to Difference > 0.05) - the original
        // 0.5 value was never once satisfied on any real ATAS capture analyzed (Lots 2-3), permanently
        // blocking Direction; 0.95 is the candidate value from the Lots 4-8 offline OOS study. See
        // AmbiguityGateThreshold's own doc comment for the reversion path and the Lot 9 report for the
        // live-ATAS validation this change still requires before any further calibration decision.
        if (decision.AmbiguityScore >= AmbiguityGateThreshold)
        {
            suppressionReason = $"Direction suppressed: decision ambiguity {decision.AmbiguityScore:F3} >= {AmbiguityGateThreshold:F2} (regime arbitration not decisive enough to trust a directional call).";
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
        if (_triggerStatus == EntryTriggerStatus.READY
            && _direction == DirectionCandidate.NO_ACTION
            && _noActionReason.HasValue)
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
