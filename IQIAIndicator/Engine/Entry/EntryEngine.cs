using System;
using IQIAIndicator.Engine.ScientificFusion;

namespace IQIAIndicator.Engine.Entry;

public sealed class EntryEngine
{
    public EntryCandidate Process(EntryContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var builder = new EntryAssessmentBuilder();
        builder.Populate(context.ScientificAssessment);
        var assessment = builder.Build(context.ScientificAssessment!);

        var candidate = new EntryCandidate(
            assessment,
            DateTime.UtcNow,
            assessment.OpportunityStatus,
            assessment.OpportunityPriority,
            assessment.OpportunityReasons ?? Array.Empty<string>(),
            assessment.Warnings ?? Array.Empty<string>(),
            assessment.Diagnostics ?? Array.Empty<string>());

        return candidate;
    }
}
