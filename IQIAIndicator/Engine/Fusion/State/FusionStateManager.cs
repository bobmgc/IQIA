using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Engine.Fusion.State;

/// <summary>
/// Stabilizes instantaneous fusion dimensions into a temporally smoothed market state.
/// </summary>
public sealed class FusionStateManager
{
    private static readonly FusionDimension[] Dimensions =
    [
        FusionDimension.Stationarity,
        FusionDimension.Persistence,
        FusionDimension.MeanReversion,
        FusionDimension.StructuralStability,
        FusionDimension.RandomWalk
    ];

    private readonly StabilizationConfiguration _configuration = StabilizationConfiguration.Default;
    private FusionResult? _previousStableResult;
    private int _updateCount;

    public FusionSnapshot Update(FusionResult rawResult, DateTime timestamp)
    {
        int changedDimensionCount = 0;
        FusionResult stableResult = _previousStableResult is null
            ? BuildInitialStableResult(rawResult)
            : BuildStableResult(rawResult, _previousStableResult, out changedDimensionCount);

        bool stateChanged = _previousStableResult is null || changedDimensionCount >= 3;
        _previousStableResult = stableResult;
        _updateCount++;

        return new FusionSnapshot
        {
            StableResult = stableResult,
            Timestamp = timestamp,
            UpdateCount = _updateCount,
            StateChanged = stateChanged
        };
    }

    private FusionResult BuildInitialStableResult(FusionResult rawResult)
    {
        var builder = new FusionResultBuilder();

        foreach (FusionDimension dimension in Dimensions)
            builder.Dimensions[dimension] = GetConfidence(rawResult, dimension);

        return builder.Build();
    }

    private FusionResult BuildStableResult(
        FusionResult rawResult,
        FusionResult previousStableResult,
        out int changedDimensionCount)
    {
        var builder = new FusionResultBuilder();
        changedDimensionCount = 0;

        foreach (FusionDimension dimension in Dimensions)
        {
            FusionConfidence rawConfidence = GetConfidence(rawResult, dimension);
            FusionConfidence previousConfidence = GetConfidence(previousStableResult, dimension);

            double smoothedValue = Smooth(rawConfidence.Value, previousConfidence.Value);
            double smoothedConfidence = Smooth(rawConfidence.Confidence, previousConfidence.Confidence);
            bool valueChanged = Math.Abs(smoothedValue - previousConfidence.Value) >= _configuration.HysteresisThreshold;
            bool confidenceChanged = Math.Abs(smoothedConfidence - previousConfidence.Confidence) >=
                _configuration.HysteresisThreshold;

            if (valueChanged)
                changedDimensionCount++;

            builder.Dimensions[dimension] = new FusionConfidence
            {
                Value = valueChanged ? smoothedValue : previousConfidence.Value,
                Confidence = confidenceChanged ? smoothedConfidence : previousConfidence.Confidence,
                Explanation = rawConfidence.Explanation
            };
        }

        return builder.Build();
    }

    private double Smooth(double newValue, double previousValue) =>
        _configuration.Alpha * ClampScore(newValue) + (1.0 - _configuration.Alpha) * ClampScore(previousValue);

    private static double ClampScore(double value) => Math.Clamp(value, 0.0, 1.0);

    private static FusionConfidence GetConfidence(FusionResult fusionResult, FusionDimension dimension) =>
        fusionResult.Dimensions.TryGetValue(dimension, out FusionConfidence? confidence)
            ? confidence
            : new FusionConfidence
            {
                Value = 0.0,
                Confidence = 0.0,
                Explanation = "Unavailable"
            };

    private sealed record StabilizationConfiguration
    {
        // Provisional EMA alpha for market state smoothing.
        public required double Alpha { get; init; }

        // Provisional hysteresis threshold for suppressing micro-variations.
        public required double HysteresisThreshold { get; init; }

        public static StabilizationConfiguration Default { get; } = new()
        {
            Alpha = 0.20,
            HysteresisThreshold = 0.03
        };
    }
}
