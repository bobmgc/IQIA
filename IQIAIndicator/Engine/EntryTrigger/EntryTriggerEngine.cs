using System;
using IQIAIndicator.Engine.Entry;

namespace IQIAIndicator.Engine.EntryTrigger;

public sealed record EntryTriggerResult(
    EntryTriggerCandidate Candidate,
    EntryTiming Timing);

public sealed class EntryTriggerEngine
{
    private readonly double _ambiguityGateThreshold;
    private readonly double _minMomentumConfidence;

    /// <summary>Production default (Sprint 15.25, Lot 14.10, P0-3): identical behaviour to every
    /// EntryTriggerEngine that existed before this lot.</summary>
    public EntryTriggerEngine() : this(EntryTriggerBuilder.AmbiguityGateThreshold, EntryTriggerBuilder.DefaultMinMomentumConfidence)
    {
    }

    /// <summary>Audit 2026-08-30 (P0-2): ambiguity-gate-only overload retained; MinMomentumConfidence
    /// keeps its production default.</summary>
    public EntryTriggerEngine(double ambiguityGateThreshold)
        : this(ambiguityGateThreshold, EntryTriggerBuilder.DefaultMinMomentumConfidence)
    {
    }

    /// <summary>Sprint 15.25 (Lot 14.10, P0-3) + audit 2026-08-30 (P0-2): threads the calibration-supplied
    /// ambiguity gate AND trend-following momentum-confidence floor down to the
    /// <see cref="EntryTriggerBuilder"/> it constructs - see <see cref="Engine.Signal.SignalEngine"/>'s
    /// constructor for the next link in the chain.</summary>
    public EntryTriggerEngine(double ambiguityGateThreshold, double minMomentumConfidence)
    {
        _ambiguityGateThreshold = ambiguityGateThreshold;
        _minMomentumConfidence = minMomentumConfidence;
    }

    public EntryTriggerResult Process(EntryTriggerContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var builder = new EntryTriggerBuilder(_ambiguityGateThreshold, _minMomentumConfidence);
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
