using System.Linq;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.Rules;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Tests.Decision;

/// <summary>
/// Contract checks for stable range compatibility from fused dimensions only.
/// </summary>
public static class StableRangeRuleTests
{
    public static void RunAll()
    {
        AssertHighScientificLowQuality();
        AssertLowScientificHighQuality();
        AssertHighScientificHighQuality();
        AssertIncompleteFusionResult();
    }

    private static void AssertHighScientificLowQuality()
    {
        DecisionResult result = Evaluate(
            stationarityValue: 1.0,
            stationarityConfidence: 0.1,
            meanReversionValue: 1.0,
            meanReversionConfidence: 0.1,
            structuralStabilityValue: 1.0,
            structuralStabilityConfidence: 0.1);

        Assert(result.State == MarketState.StableRange, "High scientific support must select stable range compatibility.");
        AssertClose(0.91, result.Confidence, "Low quality must slightly reduce a high scientific score.");
        Assert(result.TriggeredRules.Contains(nameof(StableRangeRule)), "The stable range rule must be triggered.");
        Assert(result.RejectedRules.Count == 0, "A selected stable range rule must not be rejected.");
        Assert(result.Explanation.Contains("Scientific Score : 1.000"), "The scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 0.100"), "The low quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 0.910"), "The slightly reduced final score must be shown.");
        Assert(result.Explanation.Contains("Decision Confidence : 0.100"), "Decision confidence must match quality score.");
        Assert(result.Explanation.Contains("Strong Stationarity"), "Strong stationarity must be described.");
        Assert(result.Explanation.Contains("Strong Mean Reversion"), "Strong mean reversion must be described.");
        Assert(result.Explanation.Contains("High Structural Stability"), "High structural stability must be described.");
    }

    private static void AssertLowScientificHighQuality()
    {
        DecisionResult result = Evaluate(
            stationarityValue: 0.1,
            stationarityConfidence: 1.0,
            meanReversionValue: 0.1,
            meanReversionConfidence: 1.0,
            structuralStabilityValue: 0.1,
            structuralStabilityConfidence: 1.0);

        Assert(result.State == MarketState.StableRange, "The rule still reports stable range compatibility evidence.");
        AssertClose(0.19, result.Confidence, "High quality must not inflate weak scientific support.");
        Assert(result.Explanation.Contains("Scientific Score : 0.100"), "The low scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The high quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 0.190"), "The final score must remain low.");
        Assert(result.Explanation.Contains("Decision Confidence : 1.000"), "Decision confidence must match quality score.");
        Assert(result.Explanation.Contains("Weak Stationarity"), "Weak stationarity must be described.");
        Assert(result.Explanation.Contains("Weak Mean Reversion"), "Weak mean reversion must be described.");
        Assert(result.Explanation.Contains("Low Structural Stability"), "Low structural stability must be described.");
    }

    private static void AssertHighScientificHighQuality()
    {
        DecisionResult result = Evaluate(
            stationarityValue: 1.0,
            stationarityConfidence: 1.0,
            meanReversionValue: 1.0,
            meanReversionConfidence: 1.0,
            structuralStabilityValue: 1.0,
            structuralStabilityConfidence: 1.0);

        Assert(result.State == MarketState.StableRange, "High scientific support and quality must select stable range compatibility.");
        AssertClose(1.0, result.Confidence, "High scientific support and high quality must produce a high final score.");
        Assert(result.TriggeredRules.Contains(nameof(StableRangeRule)), "The stable range rule must be triggered.");
        Assert(result.Explanation.Contains("Scientific Score : 1.000"), "The high scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The high quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 1.000"), "The high final score must be shown.");
        Assert(result.Explanation.Contains("Decision Confidence : 1.000"), "Decision confidence must match quality score.");
        Assert(result.Explanation.Contains("Strong Stationarity"), "Stationarity must remain strong.");
        Assert(result.Explanation.Contains("Strong Mean Reversion"), "Mean reversion must remain strong.");
        Assert(result.Explanation.Contains("High Structural Stability"), "Structural stability must remain high.");
    }

    private static void AssertIncompleteFusionResult()
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.Stationarity] = Confidence(value: 1.0, confidence: 1.0);
        builder.Dimensions[FusionDimension.MeanReversion] = Confidence(value: 1.0, confidence: 1.0);

        DecisionResult result = Evaluate(builder.Build());

        Assert(result.State == MarketState.Unknown, "An incomplete fusion result must not select a state.");
        Assert(result.Confidence == 0.0, "An incomplete fusion result must not produce confidence.");
        Assert(result.TriggeredRules.Count == 0, "An incomplete fusion result must not trigger the rule.");
        Assert(result.RejectedRules.Contains(nameof(StableRangeRule)), "An incomplete fusion result must reject the rule.");
        Assert(result.Explanation == "No Decision Rule", "An incomplete fusion result must keep the default no decision explanation.");
    }

    private static DecisionResult Evaluate(
        double stationarityValue,
        double stationarityConfidence,
        double meanReversionValue,
        double meanReversionConfidence,
        double structuralStabilityValue,
        double structuralStabilityConfidence)
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.Stationarity] = Confidence(stationarityValue, stationarityConfidence);
        builder.Dimensions[FusionDimension.MeanReversion] = Confidence(meanReversionValue, meanReversionConfidence);
        builder.Dimensions[FusionDimension.StructuralStability] = Confidence(
            structuralStabilityValue,
            structuralStabilityConfidence);

        return Evaluate(builder.Build());
    }

    private static DecisionResult Evaluate(FusionResult fusionResult)
    {
        var engine = new DecisionEngine([new StableRangeRule()]);
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
