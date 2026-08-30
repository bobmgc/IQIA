using System;

namespace IQIAIndicator.Engine.EntryTrigger;

public enum EntryTriggerStatus
{
    NOT_READY,
    WATCH,
    READY,
    EXPIRED,
    INVALID
}

public enum DirectionCandidate
{
    BUY_CANDIDATE,
    SELL_CANDIDATE,
    WATCH,
    NO_ACTION
}

public enum EntryTriggerReason
{
    UNKNOWN,
    INVALID_CONTEXT,
    BLOCKED,
    NOT_READY,
    WATCH,
    READY,
    EXPIRED,

    // Sprint 15.7.1: granular reasons for a NO_ACTION direction reached while TriggerStatus is
    // READY (see EntryTriggerBuilder.DetermineDirection). Observability only - none of these change
    // when Direction resolves to NO_ACTION, only whether the operator can see why.
    DYNAMIC_ZSCORE_UNAVAILABLE,
    PRICE_AT_EQUILIBRIUM,
    DECISION_AMBIGUOUS,

    // Sprint 15.25 (Lot 15.1, brief §11/§12): the regime DecisionArbitrator actually selected has no
    // scientific model registered for it (MethodologyRegistry names a methodology, but
    // ScientificModelRegistry.Resolve returns zero real, non-placeholder models for it - see
    // EntryTriggerBuilder.DetermineDirection's doc comment). Distinguishes "this regime is not yet
    // scientifically supported" from every other NO_ACTION reason above, all of which are per-bar
    // market conditions reached only for the one regime (MeanReverting) that IS supported. Never
    // means "market conditions insufficient" and never means "technical error" - see
    // Engine.ScientificFusion.ScientificCoverageStatus.NoModelCoverage, the existing, already-wired
    // signal this reason reports on (reused, not duplicated - brief §13).
    UNSUPPORTED_REGIME,

    // Audit 2026-08-30 (P0-2): trend-following (Winner == Trending) NO_ACTION reasons, analogous to
    // the mean-reversion ones above. INSUFFICIENT_MOMENTUM = TimeSeriesMomentumModel ran but the
    // multi-horizon momentum is too weak / undirected to trade; MOMENTUM_UNAVAILABLE = the model's
    // MomentumScore metric was absent from the scientific results this bar.
    INSUFFICIENT_MOMENTUM,
    MOMENTUM_UNAVAILABLE
}

public sealed record EntryTriggerAssessment(
    EntryTriggerStatus TriggerStatus,
    DirectionCandidate Direction,
    double ScientificConfidence,
    double OpportunityPriority,
    EntryTriggerReason Reason,
    double? EstimatedEquilibrium,
    double? DistanceToEquilibrium,
    DateTime Timestamp);
