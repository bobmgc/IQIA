using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.State;

namespace IQIAIndicator.Engine.Fusion.Profile;

/// <summary>
/// Computes temporal metrics from a sliding window of stabilized fusion snapshots.
/// </summary>
public sealed class FusionProfileAnalyzer
{
    private const int DefaultWindowSize = 6;

    private static readonly FusionDimension[] Dimensions =
    [
        FusionDimension.Stationarity,
        FusionDimension.Persistence,
        FusionDimension.MeanReversion,
        FusionDimension.StructuralStability,
        FusionDimension.RandomWalk
    ];

    private readonly int _windowSize;
    private readonly Queue<FusionSnapshot> _history = new();

    public FusionProfileAnalyzer() : this(DefaultWindowSize)
    {
    }

    public FusionProfileAnalyzer(int windowSize)
    {
        _windowSize = Math.Max(2, windowSize);
    }

    public FusionProfileAnalysis Analyze(FusionSnapshot snapshot)
    {
        EnqueueSnapshot(snapshot);

        if (_history.Count < 2)
            return new FusionProfileAnalysis
            {
                SnapshotCount = _history.Count,
                WindowSize = _windowSize,
                ProfileVelocity = 0.0,
                ProfileStability = 1.0,
                BehaviourConsistency = 1.0
            };

        double profileVelocity = CalculateProfileVelocity(_history);
        double profileStability = CalculateProfileStability(_history);
        double behaviourConsistency = CalculateBehaviourConsistency(profileVelocity, profileStability);

        return new FusionProfileAnalysis
        {
            SnapshotCount = _history.Count,
            WindowSize = _windowSize,
            ProfileVelocity = profileVelocity,
            ProfileStability = profileStability,
            BehaviourConsistency = behaviourConsistency
        };
    }

    private void EnqueueSnapshot(FusionSnapshot snapshot)
    {
        _history.Enqueue(snapshot);
        while (_history.Count > _windowSize)
            _history.Dequeue();
    }

    private static double CalculateProfileVelocity(IEnumerable<FusionSnapshot> snapshots)
    {
        FusionSnapshot? previous = null;
        double totalDistance = 0.0;
        int steps = 0;

        foreach (FusionSnapshot current in snapshots)
        {
            if (previous is not null)
            {
                totalDistance += SnapshotDistance(previous.StableResult, current.StableResult);
                steps++;
            }

            previous = current;
        }

        return steps == 0 ? 0.0 : Math.Clamp(totalDistance / steps, 0.0, 1.0);
    }

    private static double SnapshotDistance(FusionResult first, FusionResult second)
    {
        double distance = 0.0;

        foreach (FusionDimension dimension in Dimensions)
        {
            double valueA = GetValue(first, dimension);
            double valueB = GetValue(second, dimension);
            distance += Math.Abs(valueA - valueB);
        }

        return distance / Dimensions.Length;
    }

    private static double CalculateProfileStability(IEnumerable<FusionSnapshot> snapshots)
    {
        var values = new List<double>();

        foreach (FusionSnapshot snapshot in snapshots)
        {
            foreach (FusionDimension dimension in Dimensions)
                values.Add(GetValue(snapshot.StableResult, dimension));
        }

        if (values.Count == 0)
            return 1.0;

        double mean = values.Average();
        double totalDeviation = values.Sum(value => Math.Abs(value - mean));
        double meanAbsDeviation = totalDeviation / values.Count;

        return Math.Clamp(1.0 - meanAbsDeviation / 0.5, 0.0, 1.0);
    }

    private static double CalculateBehaviourConsistency(double profileVelocity, double profileStability)
    {
        const double stabilityWeight = 0.65;
        const double velocityWeight = 0.35;

        return Math.Clamp(
            stabilityWeight * profileStability +
            velocityWeight * (1.0 - profileVelocity),
            0.0,
            1.0);
    }

    private static double GetValue(FusionResult fusionResult, FusionDimension dimension) =>
        fusionResult.Dimensions.TryGetValue(dimension, out FusionConfidence? confidence)
            ? Math.Clamp(confidence.Value, 0.0, 1.0)
            : 0.0;
}
