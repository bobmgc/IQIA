using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.State;

namespace IQIAIndicator.Tests.Fusion;

/// <summary>
/// Contract checks for temporal stabilization of fusion dimensions.
/// </summary>
public static class FusionStateManagerTests
{
    public static void RunAll()
    {
        AssertIdenticalValuesRemainIdentical();
        AssertSmallVariationIsIgnored();
        AssertLargeVariationAppliesEma();
        AssertThreeDimensionsChanged();
        AssertSingleDimensionDoesNotChangeState();
        AssertConfidenceIsSmoothed();
    }

    private static void AssertIdenticalValuesRemainIdentical()
    {
        var manager = new FusionStateManager();
        FusionResult raw = CreateResult(0.50, 0.50, 0.50, 0.50, 0.50);

        manager.Update(raw, DateTime.UnixEpoch);
        FusionSnapshot snapshot = manager.Update(raw, DateTime.UnixEpoch.AddSeconds(1));

        AssertValue(snapshot, FusionDimension.Stationarity, 0.50, "Identical values must remain unchanged.");
        Assert(snapshot.UpdateCount == 2, "Update count must increment on each accepted snapshot.");
        Assert(!snapshot.StateChanged, "Identical values must not mark the state as changed.");
    }

    private static void AssertSmallVariationIsIgnored()
    {
        var manager = new FusionStateManager();
        manager.Update(CreateResult(0.50, 0.50, 0.50, 0.50, 0.50), DateTime.UnixEpoch);

        FusionSnapshot snapshot = manager.Update(
            CreateResult(0.60, 0.50, 0.50, 0.50, 0.50),
            DateTime.UnixEpoch.AddSeconds(1));

        AssertValue(snapshot, FusionDimension.Stationarity, 0.50, "A smoothed variation below hysteresis must be ignored.");
        Assert(!snapshot.StateChanged, "A micro-variation must not change the state.");
    }

    private static void AssertLargeVariationAppliesEma()
    {
        var manager = new FusionStateManager();
        manager.Update(CreateResult(0.50, 0.50, 0.50, 0.50, 0.50), DateTime.UnixEpoch);

        FusionSnapshot snapshot = manager.Update(
            CreateResult(1.00, 0.50, 0.50, 0.50, 0.50),
            DateTime.UnixEpoch.AddSeconds(1));

        AssertValue(snapshot, FusionDimension.Stationarity, 0.60, "A large variation must apply EMA smoothing.");
        Assert(!snapshot.StateChanged, "One changed dimension is not enough to mark a state change.");
    }

    private static void AssertThreeDimensionsChanged()
    {
        var manager = new FusionStateManager();
        manager.Update(CreateResult(0.50, 0.50, 0.50, 0.50, 0.50), DateTime.UnixEpoch);

        FusionSnapshot snapshot = manager.Update(
            CreateResult(1.00, 1.00, 1.00, 0.50, 0.50),
            DateTime.UnixEpoch.AddSeconds(1));

        Assert(snapshot.StateChanged, "Three changed dimensions must mark the state as changed.");
        AssertValue(snapshot, FusionDimension.Stationarity, 0.60, "Stationarity EMA must be applied.");
        AssertValue(snapshot, FusionDimension.Persistence, 0.60, "Persistence EMA must be applied.");
        AssertValue(snapshot, FusionDimension.MeanReversion, 0.60, "MeanReversion EMA must be applied.");
    }

    private static void AssertSingleDimensionDoesNotChangeState()
    {
        var manager = new FusionStateManager();
        manager.Update(CreateResult(0.50, 0.50, 0.50, 0.50, 0.50), DateTime.UnixEpoch);

        FusionSnapshot snapshot = manager.Update(
            CreateResult(0.50, 0.50, 0.50, 1.00, 0.50),
            DateTime.UnixEpoch.AddSeconds(1));

        Assert(!snapshot.StateChanged, "A single changed dimension must not mark the state as changed.");
        Assert(snapshot.StableResult.Dimensions[FusionDimension.StructuralStability].Explanation.Contains(
            "BehaviourConsistency="), "StructuralStability must be recalculated from the profile.");
    }

    private static void AssertConfidenceIsSmoothed()
    {
        var manager = new FusionStateManager();
        manager.Update(CreateResult(0.50, 0.50, 0.50, 0.50, 0.50, confidence: 0.50), DateTime.UnixEpoch);

        FusionSnapshot snapshot = manager.Update(
            CreateResult(0.50, 0.50, 0.50, 0.50, 0.50, confidence: 1.00),
            DateTime.UnixEpoch.AddSeconds(1));

        AssertConfidence(snapshot, FusionDimension.Stationarity, 0.60, "Confidence must be smoothed with the same EMA.");
        Assert(!snapshot.StateChanged, "Confidence-only changes must not mark market state as changed.");
    }

    private static FusionResult CreateResult(
        double stationarity,
        double persistence,
        double meanReversion,
        double structuralStability,
        double randomWalk,
        double confidence = 1.0)
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.Stationarity] = Confidence(stationarity, confidence);
        builder.Dimensions[FusionDimension.Persistence] = Confidence(persistence, confidence);
        builder.Dimensions[FusionDimension.MeanReversion] = Confidence(meanReversion, confidence);
        builder.Dimensions[FusionDimension.StructuralStability] = Confidence(structuralStability, confidence);
        builder.Dimensions[FusionDimension.RandomWalk] = Confidence(randomWalk, confidence);
        return builder.Build();
    }

    private static FusionConfidence Confidence(double value, double confidence) => new()
    {
        Value = value,
        Confidence = confidence,
        Explanation = string.Empty
    };

    private static void AssertValue(
        FusionSnapshot snapshot,
        FusionDimension dimension,
        double expected,
        string message) =>
        AssertClose(expected, snapshot.StableResult.Dimensions[dimension].Value, message);

    private static void AssertConfidence(
        FusionSnapshot snapshot,
        FusionDimension dimension,
        double expected,
        string message) =>
        AssertClose(expected, snapshot.StableResult.Dimensions[dimension].Confidence, message);

    private static void AssertClose(double expected, double actual, string message)
    {
        if (Math.Abs(expected - actual) > 1e-12)
            throw new InvalidOperationException($"{message} Expected={expected:F3}; Actual={actual:F3}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
