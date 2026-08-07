using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Engine.Fusion.State;

/// <summary>
/// Immutable stabilized fusion state consumed by downstream engines.
/// </summary>
public sealed record FusionSnapshot
{
    public required FusionResult StableResult { get; init; }

    public required DateTime Timestamp { get; init; }

    public required int UpdateCount { get; init; }

    public required bool StateChanged { get; init; }
}
