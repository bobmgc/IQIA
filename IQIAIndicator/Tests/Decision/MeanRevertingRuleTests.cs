using System.Linq;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.Rules;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Tests.Decision;

/// <summary>
/// Contract checks for mean reverting regime compatibility from fused dimensions only.
/// </summary>
public static class MeanRevertingRuleTests
{
    public static void RunAll()
    {
        AssertStrongMeanReversion();
        AssertWeakStationarity();
        AssertHighPersistencePenalty();
        AssertWeakStructuralStability();
        AssertIncompleteFusionResult();
    }

    private static void AssertStrongMeanReversion()
    {
        DecisionResult result = Evaluate(
            meanReversionValue: 1.0,
            meanReversionConfidence: 1.0,
            stationarityValue: 1.0,
            stationarityConfidence: 1.0,
            persistenceValue: 0.0,
            persistenceConfidence: 1.0,
            structuralStabilityValue: 1.0,
            structuralStabilityConfidence: 1.0);

        Assert(result.State == MarketState.MeanReverting, "Strong mean reversion evidence must select the mean reverting state.");
        AssertClose(1.0, result.Confidence, "Strong mean reversion evidence must produce a high final score.");
        Assert(result.TriggeredRules.Contains(nameof(MeanRevertingRule)), "The mean reverting rule must be triggered.");
        Assert(result.RejectedRules.Count == 0, "A selected mean reverting rule must not be rejected.");
        Assert(result.Explanation.Contains("Scientific Score : 1.000"), "The scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Decision Confidence : 1.000"), "Decision confidence must match quality score.");
        Assert(result.Explanation.Contains("Final Score : 1.000"), "The final score must be shown.");
        Assert(result.Explanation.Contains("Strong Mean Reversion"), "Strong mean reversion must be described.");
        Assert(result.Explanation.Contains("Strong Stationarity"), "Strong stationarity must be described.");
        Assert(result.Explanation.Contains("Low Persistence"), "Low persistence must be described.");
        Assert(result.Explanation.Contains("High Structural Stability"), "High structural stability must be described.");
    }

    private static void AssertWeakStationarity()
    {
        DecisionResult result = Evaluate(
            meanReversionValue: 1.0,
            meanReversionConfidence: 1.0,
            stationarityValue: 0.0,
            stationarityConfidence: 1.0,
            persistenceValue: 0.0,
            persistenceConfidence: 1.0,
            structuralStabilityValue: 1.0,
            structuralStabilityConfidence: 1.0);

        Assert(result.State == MarketState.MeanReverting, "The rule still reports mean reversion compatibility evidence.");
        AssertClose(0.73, result.Confidence, "Weak stationarity must reduce the final score.");
        Assert(result.Explanation.Contains("Scientific Score : 0.700"), "The reduced scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 0.730"), "The reduced final score must be shown.");
        Assert(result.Explanation.Contains("Strong Mean Reversion"), "Mean reversion must remain strong.");
        Assert(result.Explanation.Contains("Weak Stationarity"), "Weak stationarity must be described.");
        Assert(result.Explanation.Contains("Low Persistence"), "Low persistence must be described.");
        Assert(result.Explanation.Contains("High Structural Stability"), "Structural stability must remain high.");
    }

    private static void AssertHighPersistencePenalty()
    {
        DecisionResult result = Evaluate(
            meanReversionValue: 1.0,
            meanReversionConfidence: 1.0,
            stationarityValue: 1.0,
            stationarityConfidence: 1.0,
            persistenceValue: 1.0,
            persistenceConfidence: 1.0,
            structuralStabilityValue: 1.0,
            structuralStabilityConfidence: 1.0);

        Assert(result.State == MarketState.MeanReverting, "The rule still reports mean reversion compatibility evidence.");
        AssertClose(0.82, result.Confidence, "High persistence must penalize mean reversion compatibility.");
        Assert(result.Explanation.Contains("Scientific Score : 0.800"), "The penalized scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 0.820"), "The penalized final score must be shown.");
        Assert(result.Explanation.Contains("Strong Mean Reversion"), "Mean reversion must remain strong.");
        Assert(result.Explanation.Contains("Strong Stationarity"), "Stationarity must remain strong.");
        Assert(result.Explanation.Contains("High Persistence"), "High persistence must be described.");
        Assert(result.Explanation.Contains("High Structural Stability"), "Structural stability must remain high.");
    }

    private static void AssertWeakStructuralStability()
    {
        DecisionResult result = Evaluate(
            meanReversionValue: 1.0,
            meanReversionConfidence: 1.0,
            stationarityValue: 1.0,
            stationarityConfidence: 1.0,
            persistenceValue: 0.0,
            persistenceConfidence: 1.0,
            structuralStabilityValue: 0.0,
            structuralStabilityConfidence: 1.0);

        Assert(result.State == MarketState.MeanReverting, "The rule still reports mean reversion compatibility evidence.");
        AssertClose(0.91, result.Confidence, "Weak structural stability must reduce the final score.");
        Assert(result.Explanation.Contains("Scientific Score : 0.900"), "The reduced scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 0.910"), "The reduced final score must be shown.");
        Assert(result.Explanation.Contains("Strong Mean Reversion"), "Mean reversion must remain strong.");
        Assert(result.Explanation.Contains("Strong Stationarity"), "Stationarity must remain strong.");
        Assert(result.Explanation.Contains("Low Persistence"), "Persistence must remain low.");
        Assert(result.Explanation.Contains("Low Structural Stability"), "Low structural stability must be described.");
    }

    private static void AssertIncompleteFusionResult()
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.MeanReversion] = Confidence(value: 1.0, confidence: 1.0);
        builder.Dimensions[FusionDimension.Stationarity] = Confidence(value: 1.0, confidence: 1.0);
        builder.Dimensions[FusionDimension.Persistence] = Confidence(value: 0.0, confidence: 1.0);

        DecisionResult result = Evaluate(builder.Build());

        Assert(result.State == MarketState.Unknown, "An incomplete fusion result must not select a state.");
        Assert(result.Confidence == 0.0, "An incomplete fusion result must not produce confidence.");
        Assert(result.TriggeredRules.Count == 0, "An incomplete fusion result must not trigger the rule.");
        Assert(result.RejectedRules.Contains(nameof(MeanRevertingRule)), "An incomplete fusion result must reject the rule.");
        Assert(result.Explanation == "No Decision Rule", "An incomplete fusion result must keep the default no decision explanation.");
    }

    private static DecisionResult Evaluate(
        double meanReversionValue,
        double meanReversionConfidence,
        double stationarityValue,
        double stationarityConfidence,
        double persistenceValue,
        double persistenceConfidence,
        double structuralStabilityValue,
        double structuralStabilityConfidence)
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.MeanReversion] = Confidence(meanReversionValue, meanReversionConfidence);
        builder.Dimensions[FusionDimension.Stationarity] = Confidence(stationarityValue, stationarityConfidence);
        builder.Dimensions[FusionDimension.Persistence] = Confidence(persistenceValue, persistenceConfidence);
        builder.Dimensions[FusionDimension.StructuralStability] = Confidence(
            structuralStabilityValue,
            structuralStabilityConfidence);

        return Evaluate(builder.Build());
    }

    private static DecisionResult Evaluate(FusionResult fusionResult)
    {
        var engine = new DecisionEngine([new MeanRevertingRule()]);
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
