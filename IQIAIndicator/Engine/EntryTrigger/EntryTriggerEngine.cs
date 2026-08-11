using System;
using IQIAIndicator.Engine.Entry;

namespace IQIAIndicator.Engine.EntryTrigger;

public sealed record EntryTriggerResult(
    EntryTriggerCandidate Candidate,
    EntryTiming Timing);

public sealed class EntryTriggerEngine
{
    public EntryTriggerResult Process(EntryTriggerContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var builder = new EntryTriggerBuilder();
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
