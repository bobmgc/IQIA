using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace IQIAIndicator.Engine.EntryTrigger;

public sealed class EntryTriggerPresentationBuilder
{
    private readonly List<string> _warnings = new();
    private readonly List<string> _diagnostics = new();

    public EntryTriggerPresentation Build(EntryTriggerCandidate candidate)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        AddDistinct(_warnings, candidate.Warnings);
        AddDistinct(_diagnostics, candidate.Diagnostics);

        return new EntryTriggerPresentation(
            candidate.Assessment.TriggerStatus,
            candidate.Assessment.Direction,
            candidate.Assessment.ScientificConfidence,
            candidate.Assessment.OpportunityPriority,
            candidate.Assessment.Reason,
            candidate.Assessment.EstimatedEquilibrium,
            candidate.Assessment.DistanceToEquilibrium,
            candidate.CurrentPrice,
            candidate.Assessment.Timestamp,
            ImmutableArray.CreateRange(_warnings),
            ImmutableArray.CreateRange(_diagnostics));
    }

    private static void AddDistinct(List<string> target, IReadOnlyList<string> source)
    {
        if (source is null)
        {
            return;
        }

        foreach (string item in source)
        {
            if (!string.IsNullOrWhiteSpace(item) && !target.Contains(item))
            {
                target.Add(item);
            }
        }
    }
}
