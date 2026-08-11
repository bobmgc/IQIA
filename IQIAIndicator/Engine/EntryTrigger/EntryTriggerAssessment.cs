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
    EXPIRED
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
