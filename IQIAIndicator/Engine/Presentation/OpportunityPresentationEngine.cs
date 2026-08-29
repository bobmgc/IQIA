using System;

namespace IQIAIndicator.Engine.Presentation;

public sealed class OpportunityPresentationEngine
{
    public OpportunityPresentation Process(OpportunityPresentationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var builder = new OpportunityPresentationBuilder();
        builder.Populate(context.ChartAnnotationCandidate);

        return builder.Build();
    }
}
