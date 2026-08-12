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
    DECISION_AMBIGUOUS
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
