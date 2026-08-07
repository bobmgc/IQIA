using IQIAIndicator.Engine.Decision.States;

namespace IQIAIndicator.Engine.Methodology.Core;

public sealed class MethodologyRegistry
{
    public QuantitativeMethodology Resolve(MarketState marketState)
    {
        return marketState switch
        {
            MarketState.MeanReverting => new QuantitativeMethodology(
                "MeanReversionMethodology",
                "Methodology for mean reversion-compatible market behaviour.",
                "Ornstein-Uhlenbeck",
                new[] { "Kalman Filter", "Dynamic Z-Score", "Volatility Models", "BOCPD" },
                "SPRT",
                new[] { "MeanReverting" },
                "1.0",
                new[] { "Bayesian Decision Theory", "Market Profile" }),
            MarketState.Trending => new QuantitativeMethodology(
                "TrendFollowingMethodology",
                "Methodology for trend-compatible market behaviour.",
                "Time Series Momentum",
                new[] { "Volatility Models", "BOCPD" },
                "SPRT",
                new[] { "Trending" },
                "1.0",
                new[] { "Momentum Filters" }),
            MarketState.StructuralBreak => new QuantitativeMethodology(
                "StructuralBreakMethodology",
                "Methodology for structural break-compatible market behaviour.",
                "BOCPD",
                new[] { "Volatility Models" },
                "SPRT",
                new[] { "StructuralBreak" },
                "1.0",
                new[] { "Sequential Detection" }),
            MarketState.RandomWalk => new QuantitativeMethodology(
                "RandomWalkMethodology",
                "Methodology for random walk-compatible market behaviour.",
                "Random Walk Null Model",
                Array.Empty<string>(),
                "SPRT",
                new[] { "RandomWalk" },
                "1.0",
                Array.Empty<string>()),
            _ => new QuantitativeMethodology(
                "UnknownMethodology",
                "No methodology is catalogued for the current market behaviour.",
                "Unavailable",
                Array.Empty<string>(),
                "Unavailable",
                Array.Empty<string>(),
                "1.0",
                Array.Empty<string>())
        };
    }
}
