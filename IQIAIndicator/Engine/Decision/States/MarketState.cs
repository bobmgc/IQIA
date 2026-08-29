namespace IQIAIndicator.Engine.Decision.States;

public enum MarketState
{
    Unknown,
    StableRange,
    MeanReverting,
    Trending,
    Transitional,
    StructuralBreak,
    RandomWalk
}