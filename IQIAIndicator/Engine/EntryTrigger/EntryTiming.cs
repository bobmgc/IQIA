using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.Entry;

namespace IQIAIndicator.Engine.EntryTrigger;

public sealed record EntryTiming(
    EntryTriggerStatus TriggerStatus,
    DirectionCandidate Direction,
    double ScientificConfidence,
    double OpportunityPriority,
    EntryTriggerReason Reason,
    OpportunityStatus OpportunityStatus,
    IReadOnlyList<string> OpportunityReasons,
    double? EstimatedEquilibrium,
    decimal CurrentPrice,
    DateTime Timestamp);
