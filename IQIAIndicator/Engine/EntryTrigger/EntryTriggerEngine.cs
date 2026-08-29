using System;
using IQIAIndicator.Engine.Entry;

namespace IQIAIndicator.Engine.EntryTrigger;

public sealed record EntryTriggerResult(
    EntryTriggerCandidate Candidate,
    EntryTiming Timing);

public sealed class EntryTriggerEngine
{
    private readonly double _ambiguityGateThreshold;

    /// <summary>Production default (Sprint 15.25, Lot 14.10, P0-3): identical behaviour to every
    /// EntryTriggerEngine that existed before this lot.</summary>
    public EntryTriggerEngine() : this(EntryTriggerBuilder.AmbiguityGateThreshold)
    {
    }

    /// <summary>Sprint 15.25 (Lot 14.10, P0-3): threads a calibration-supplied ambiguity gate threshold
    /// down to the <see cref="EntryTriggerBuilder"/> it constructs - see
    /// <see cref="Engine.Signal.SignalEngine"/>'s constructor for the next link in the chain.</summary>
    public EntryTriggerEngine(double ambiguityGateThreshold)
    {
        _ambiguityGateThreshold = ambiguityGateThreshold;
    }

    public EntryTriggerResult Process(EntryTriggerContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var builder = new EntryTriggerBuilder(_ambiguityGateThreshold);
        EntryTriggerCandidate candidate = builder.Build(context);
        var timing = new EntryTiming(
            candidate.Assessment.TriggerStatus,
            candidate.Assessment.Direction,
            candidate.Assessment.ScientificConfidence,
            candidate.Assessment.OpportunityPriority,
            candidate.Assessment.Reason,
            candidate.EntryCandidate.OpportunityStatus,
            candidate.EntryCandidate.OpportunityReasons,
            candidate.Assessment.EstimatedEquilibrium,
            candidate.CurrentPrice,
            candidate.Assessment.Timestamp);

        return new EntryTriggerResult(candidate, timing);
    }
}
