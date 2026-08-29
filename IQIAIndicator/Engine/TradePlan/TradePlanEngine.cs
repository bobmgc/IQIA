using System;

namespace IQIAIndicator.Engine.TradePlan;

public sealed class TradePlanEngine
{
    public TradePlan Process(TradePlanContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return new TradePlanBuilder().Build(context);
    }
}
