namespace IQIAIndicator.Engine.Fusion.Profile;

/// <summary>
/// Aggregated temporal metrics produced from a sliding window of fusion snapshots.
/// </summary>
public sealed record FusionProfileAnalysis
{
    public static FusionProfileAnalysis Empty { get; } = new()
    {
        ProfileVelocity = 0.0,
        ProfileStability = 1.0,
        BehaviourConsistency = 1.0,
        SnapshotCount = 0,
        WindowSize = 0
    };

    public required int SnapshotCount { get; init; }

    public required int WindowSize { get; init; }

    public required double ProfileVelocity { get; init; }

    public required double ProfileStability { get; init; }

    public required double BehaviourConsistency { get; init; }
}
