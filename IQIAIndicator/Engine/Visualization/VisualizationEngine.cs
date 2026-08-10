using System;

namespace IQIAIndicator.Engine.Visualization;

public sealed class VisualizationEngine
{
    public VisualizationCandidate Process(VisualizationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var builder = new VisualizationAssessmentBuilder();
        builder.Populate(context.EntryCandidate);
        var assessment = builder.Build();

        var candidate = new VisualizationCandidate(
            assessment,
            DateTime.UtcNow,
            assessment.Warnings ?? Array.Empty<string>(),
            assessment.Diagnostics ?? Array.Empty<string>());

        return candidate;
    }
}
