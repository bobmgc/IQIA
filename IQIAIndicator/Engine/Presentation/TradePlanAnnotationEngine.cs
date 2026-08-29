using System;

namespace IQIAIndicator.Engine.Presentation;

public sealed class TradePlanAnnotationEngine
{
    public TradePlanAnnotationCandidate Process(TradePlanAnnotationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var builder = new TradePlanAnnotationBuilder();
        builder.Populate(context.TradePlan);

        return builder.Build();
    }
}
