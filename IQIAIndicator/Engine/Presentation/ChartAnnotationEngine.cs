using System;

namespace IQIAIndicator.Engine.Presentation;

public sealed class ChartAnnotationEngine
{
    public ChartAnnotationCandidate Process(ChartAnnotationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var builder = new ChartAnnotationBuilder();
        builder.Populate(context.VisualizationCandidate);

        return builder.Build();
    }
}
