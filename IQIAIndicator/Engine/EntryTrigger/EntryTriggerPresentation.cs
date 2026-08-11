using System;
using System.Collections.Immutable;

namespace IQIAIndicator.Engine.EntryTrigger;

public sealed record EntryTriggerPresentation(
    EntryTriggerStatus TriggerStatus,
    DirectionCandidate Direction,
    double ScientificConfidence,
    double OpportunityPriority,
    EntryTriggerReason Reason,
    double? EstimatedEquilibrium,
    double? DistanceToEquilibrium,
    decimal CurrentPrice,
    DateTime Timestamp,
    ImmutableArray<string> Warnings,
    ImmutableArray<string> Diagnostics)
{
    public ImmutableDictionary<string, object> ToDictionary()
    {
        object estimated = EstimatedEquilibrium is double d ? (object)d : (object)DBNull.Value;
        object distance = DistanceToEquilibrium is double dd ? (object)dd : (object)DBNull.Value;
        object warnings = Warnings.IsDefault ? Array.Empty<string>() : (object)Warnings;
        object diagnostics = Diagnostics.IsDefault ? Array.Empty<string>() : (object)Diagnostics;

        return ImmutableDictionary<string, object>.Empty
            .Add("TriggerStatus", TriggerStatus.ToString())
            .Add("Direction", Direction.ToString())
            .Add("ScientificConfidence", ScientificConfidence)
            .Add("OpportunityPriority", OpportunityPriority)
            .Add("Reason", Reason.ToString())
            .Add("EstimatedEquilibrium", estimated)
            .Add("DistanceToEquilibrium", distance)
            .Add("CurrentPrice", CurrentPrice)
            .Add("Timestamp", Timestamp)
            .Add("Warnings", warnings)
            .Add("Diagnostics", diagnostics);
    }
}
