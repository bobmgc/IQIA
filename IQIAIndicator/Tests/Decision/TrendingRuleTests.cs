using System.Linq;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.Rules;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Tests.Decision;

/// <summary>
/// Contract checks for directional market compatibility from fused dimensions only.
/// </summary>
public static class TrendingRuleTests
{
    public static void RunAll()
    {
        AssertStrongTrend();
        AssertWeakPersistence();
        AssertUnstableStructure();
        AssertHighStationarityPenalty();
        AssertIncompleteFusionResult();
    }

    private static void AssertStrongTrend()
    {
        DecisionResult result = Evaluate(
            persistenceValue: 1.0,
            persistenceConfidence: 1.0,
            structuralStabilityValue: 1.0,
            structuralStabilityConfidence: 1.0,
            stationarityValue: 0.0,
            stationarityConfidence: 1.0);

        Assert(result.State == MarketState.Trending, "Strong directional evidence must select the trending state.");
        AssertClose(1.0, result.Confidence, "Strong directional evidence must produce a high final score.");
        Assert(result.TriggeredRules.Contains(nameof(TrendingRule)), "The trending rule must be triggered.");
        Assert(result.RejectedRules.Count == 0, "A selected trending rule must not be rejected.");
        Assert(result.Explanation.Contains("Scientific Score : 1.000"), "The scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Decision Confidence : 1.000"), "Decision confidence must match quality score.");
        Assert(result.Explanation.Contains("Final Score : 1.000"), "The final score must be shown.");
        Assert(result.Explanation.Contains("Strong Persistence"), "Strong persistence must be described.");
        Assert(result.Explanation.Contains("High Structural Stability"), "High structural stability must be described.");
        Assert(result.Explanation.Contains("Low Stationarity"), "Low stationarity must be described.");
    }

    private static void AssertWeakPersistence()
    {
        DecisionResult result = Evaluate(
            persistenceValue: 0.0,
            persistenceConfidence: 1.0,
            structuralStabilityValue: 1.0,
            structuralStabilityConfidence: 1.0,
            stationarityValue: 0.0,
            stationarityConfidence: 1.0);

        Assert(result.State == MarketState.Trending, "The rule still reports directional market compatibility evidence.");
        AssertClose(0.55, result.Confidence, "Weak persistence must reduce the final score.");
        Assert(result.Explanation.Contains("Scientific Score : 0.500"), "The reduced scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 0.550"), "The reduced final score must be shown.");
        Assert(result.Explanation.Contains("Weak Persistence"), "Weak persistence must be described.");
        Assert(result.Explanation.Contains("High Structural Stability"), "Structural stability must remain high.");
        Assert(result.Explanation.Contains("Low Stationarity"), "Low stationarity must be described.");
    }

    private static void AssertUnstableStructure()
    {
        DecisionResult result = Evaluate(
            persistenceValue: 1.0,
            persistenceConfidence: 1.0,
            structuralStabilityValue: 0.0,
            structuralStabilityConfidence: 1.0,
            stationarityValue: 0.0,
            stationarityConfidence: 1.0);

        Assert(result.State == MarketState.Trending, "The rule still reports directional market compatibility evidence.");
        AssertClose(0.73, result.Confidence, "Unstable structure must reduce the final score.");
        Assert(result.Explanation.Contains("Scientific Score : 0.700"), "The reduced scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 0.730"), "The reduced final score must be shown.");
        Assert(result.Explanation.Contains("Strong Persistence"), "Persistence must remain strong.");
        Assert(result.Explanation.Contains("Low Structural Stability"), "Low structural stability must be described.");
        Assert(result.Explanation.Contains("Low Stationarity"), "Low stationarity must be described.");
    }

    private static void AssertHighStationarityPenalty()
    {
        DecisionResult result = Evaluate(
            persistenceValue: 1.0,
            persistenceConfidence: 1.0,
            structuralStabilityValue: 1.0,
            structuralStabilityConfidence: 1.0,
            stationarityValue: 1.0,
            stationarityConfidence: 1.0);

        Assert(result.State == MarketState.Trending, "The rule still reports directional market compatibility evidence.");
        AssertClose(0.82, result.Confidence, "High stationarity must penalize directional compatibility.");
        Assert(result.Explanation.Contains("Scientific Score : 0.800"), "The penalized scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 0.820"), "The penalized final score must be shown.");
        Assert(result.Explanation.Contains("Strong Persistence"), "Persistence must remain strong.");
        Assert(result.Explanation.Contains("High Structural Stability"), "Structural stability must remain high.");
        Assert(result.Explanation.Contains("High Stationarity"), "High stationarity must be described.");
    }

    private static void AssertIncompleteFusionResult()
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.Persistence] = Confidence(value: 1.0, confidence: 1.0);
        builder.Dimensions[FusionDimension.StructuralStability] = Confidence(value: 1.0, confidence: 1.0);

        DecisionResult result = Evaluate(builder.Build());

        Assert(result.State == MarketState.Unknown, "An incomplete fusion result must not select a state.");
        Assert(result.Confidence == 0.0, "An incomplete fusion result must not produce confidence.");
        Assert(result.TriggeredRules.Count == 0, "An incomplete fusion result must not trigger the rule.");
        Assert(result.RejectedRules.Contains(nameof(TrendingRule)), "An incomplete fusion result must reject the rule.");
        Assert(result.Explanation == "No Decision Rule", "An incomplete fusion result must keep the default no decision explanation.");
    }

    private static DecisionResult Evaluate(
        double persistenceValue,
        double persistenceConfidence,
        double structuralStabilityValue,
        double structuralStabilityConfidence,
        double stationarityValue,
        double stationarityConfidence)
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.Persistence] = Confidence(persistenceValue, persistenceConfidence);
        builder.Dimensions[FusionDimension.StructuralStability] = Confidence(
            structuralStabilityValue,
            structuralStabilityConfidence);
        builder.Dimensions[FusionDimension.Stationarity] = Confidence(stationarityValue, stationarityConfidence);

        return Evaluate(builder.Build());
    }

    private static DecisionResult Evaluate(FusionResult fusionResult)
    {
        var engine = new DecisionEngine([new TrendingRule()]);
        return engine.Evaluate(new DecisionContext
        {
            FusionResult = fusionResult,
            Evidence = null!
        });
    }

    private static FusionConfidence Confidence(double value, double confidence) => new()
    {
        Value = value,
        Confidence = confidence,
        Explanation = string.Empty
    };

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
