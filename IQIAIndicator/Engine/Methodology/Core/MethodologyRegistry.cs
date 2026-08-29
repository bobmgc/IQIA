using IQIAIndicator.Engine.Decision.States;

namespace IQIAIndicator.Engine.Methodology.Core;

public sealed class MethodologyRegistry
{
    /// <summary>
    /// Sprint 15.5 (C3): StableRange and Transitional are real, reachable <see cref="MarketState"/>
    /// values - StableRangeRule is registered in the live DecisionEngine (IQIAIndicator.cs), so
    /// StableRange can genuinely win an arbitration, not just Unknown/invalid states. Both previously
    /// fell through to the same default case as MarketState.Unknown ("UnknownMethodology"), making a
    /// real-but-uncovered regime indistinguishable from a genuinely unrecognized one (Sprint 15.4
    /// audit finding BC-04). Neither has a methodology-specific model implemented yet (audited against
    /// Engine/ScientificModels: no StableRange- or Transitional-specific model exists, and inventing
    /// one is out of this sprint's scope), so both now resolve to their own explicitly-named,
    /// explicitly-unsupported methodology instead of silently sharing UnknownMethodology's bucket.
    /// UnknownMethodology is reserved for MarketState.Unknown and any future/invalid enum value.
    /// </summary>
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
            MarketState.StableRange => new QuantitativeMethodology(
                "StableRangeMethodology",
                "No scientific methodology is implemented yet for stable-range market behaviour. Explicitly unsupported, not to be confused with an unrecognized regime.",
                "Unavailable",
                Array.Empty<string>(),
                "Unavailable",
                new[] { "StableRange" },
                "1.0",
                Array.Empty<string>()),
            MarketState.Transitional => new QuantitativeMethodology(
                "TransitionalMethodology",
                "No scientific methodology is implemented yet for transitional market behaviour. Explicitly unsupported, not to be confused with an unrecognized regime.",
                "Unavailable",
                Array.Empty<string>(),
                "Unavailable",
                new[] { "Transitional" },
                "1.0",
                Array.Empty<string>()),
            // MarketState.Unknown and any future/invalid value: genuinely unrecognized regime, distinct
            // from the two explicitly-unsupported-but-known cases above.
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
