using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Profile;
using IQIAIndicator.Engine.Fusion.Rules;

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
    private readonly FusionProfileAnalyzer _profileAnalyzer = new();
    private readonly StructuralStabilityRule _structuralStabilityRule = new();

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

        FusionSnapshot snapshot = new()
        {
            StableResult = stableResult,
            Timestamp = timestamp,
            UpdateCount = _updateCount,
            StateChanged = stateChanged
        };

        FusionProfileAnalysis analysis = _profileAnalyzer.Analyze(snapshot);
        FusionConfidence stabilityConfidence = _structuralStabilityRule.EvaluateAnalysis(analysis);
        FusionResult adjustedStableResult = ReplaceStructuralStability(stableResult, stabilityConfidence);

        return snapshot with
        {
            StableResult = adjustedStableResult,
            ProfileAnalysis = analysis
        };
    }

    private static FusionResult ReplaceStructuralStability(FusionResult fusionResult, FusionConfidence stabilityConfidence)
    {
        var builder = new FusionResultBuilder();

        foreach (KeyValuePair<FusionDimension, FusionConfidence> entry in fusionResult.Dimensions)
        {
            builder.Dimensions[entry.Key] = entry.Value;
        }

        builder.Dimensions[FusionDimension.StructuralStability] = stabilityConfidence;
        return builder.Build();
    }

    private FusionResult BuildInitialStableResult(FusionResult rawResult)
    {
        var builder = new FusionResultBuilder();

        foreach (FusionDimension dimension in Dimensions)
        {
            if (dimension == FusionDimension.StructuralStability &&
                !rawResult.Dimensions.ContainsKey(FusionDimension.StructuralStability))
            {
                builder.Dimensions[dimension] = new FusionConfidence
                {
                    Value = 1.0,
                    Confidence = 1.0,
                    Explanation = "Initial structural stability default."
                };
                continue;
            }

            builder.Dimensions[dimension] = GetConfidence(rawResult, dimension);
        }

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
            FusionConfidence previousConfidence = GetConfidence(previousStableResult, dimension);
            FusionConfidence newConfidence;

            if (dimension == FusionDimension.StructuralStability)
            {
                newConfidence = previousConfidence;
            }
            else
            {
                FusionConfidence rawConfidence = GetConfidence(rawResult, dimension);
                double smoothedValue = Smooth(rawConfidence.Value, previousConfidence.Value);
                double smoothedConfidence = Smooth(rawConfidence.Confidence, previousConfidence.Confidence);
                bool valueChanged = Math.Abs(smoothedValue - previousConfidence.Value) >= _configuration.HysteresisThreshold;
                bool confidenceChanged = Math.Abs(smoothedConfidence - previousConfidence.Confidence) >=
                    _configuration.HysteresisThreshold;

                if (valueChanged)
                    changedDimensionCount++;

                newConfidence = new FusionConfidence
                {
                    Value = valueChanged ? smoothedValue : previousConfidence.Value,
                    Confidence = confidenceChanged ? smoothedConfidence : previousConfidence.Confidence,
                    Explanation = rawConfidence.Explanation,
                    // Availability reflects THIS bar's raw evidence, independent of what the smoothed
                    // Value/Confidence numbers carry forward. Without this, a missing-evidence bar's
                    // IsAvailable=false would default back to true on the very next reconstruction,
                    // silently defeating the Decision-layer fix downstream (see Sprint 14 / DEC-01,
                    // FUS-02) even though DecisionEngine only ever reads this stable, smoothed result.
                    IsAvailable = rawConfidence.IsAvailable
                };
            }

            builder.Dimensions[dimension] = newConfidence;
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
                Explanation = "Unavailable",
                IsAvailable = false
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
