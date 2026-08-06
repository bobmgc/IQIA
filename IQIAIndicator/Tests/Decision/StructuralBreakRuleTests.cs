using System.Linq;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.Rules;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Tests.Decision;

/// <summary>
/// Contract checks for structural break compatibility from fused dimensions only.
/// </summary>
public static class StructuralBreakRuleTests
{
    public static void RunAll()
    {
        AssertObviousStructuralBreak();
        AssertHighStructuralStabilityPenalty();
        AssertHighPersistencePenalty();
        AssertHighMeanReversionPenalty();
        AssertIncompleteFusionResult();
    }

    private static void AssertObviousStructuralBreak()
    {
        DecisionResult result = Evaluate(
            structuralStabilityValue: 0.0,
            structuralStabilityConfidence: 1.0,
            persistenceValue: 0.0,
            persistenceConfidence: 1.0,
            meanReversionValue: 0.0,
            meanReversionConfidence: 1.0,
            stationarityValue: 0.0,
            stationarityConfidence: 1.0);

        Assert(result.State == MarketState.StructuralBreak, "Low coherent behavior must select the structural break state.");
        AssertClose(1.0, result.Confidence, "An obvious structural break must produce a high final score.");
        Assert(result.TriggeredRules.Contains(nameof(StructuralBreakRule)), "The structural break rule must be triggered.");
        Assert(result.RejectedRules.Count == 0, "A selected structural break rule must not be rejected.");
        Assert(result.Explanation.Contains("Scientific Score : 1.000"), "The scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Decision Confidence : 1.000"), "Decision confidence must match quality score.");
        Assert(result.Explanation.Contains("Final Score : 1.000"), "The final score must be shown.");
        Assert(result.Explanation.Contains("Low Structural Stability"), "Low structural stability must be described.");
        Assert(result.Explanation.Contains("Weak Persistence"), "Weak persistence must be described.");
        Assert(result.Explanation.Contains("Weak Mean Reversion"), "Weak mean reversion must be described.");
        Assert(result.Explanation.Contains("Weak Stationarity"), "Weak stationarity must be described.");
        Assert(result.Explanation.Contains("Possible Regime Transition"), "The transition hypothesis must be explicit.");
    }

    private static void AssertHighStructuralStabilityPenalty()
    {
        DecisionResult result = Evaluate(
            structuralStabilityValue: 1.0,
            structuralStabilityConfidence: 1.0,
            persistenceValue: 0.0,
            persistenceConfidence: 1.0,
            meanReversionValue: 0.0,
            meanReversionConfidence: 1.0,
            stationarityValue: 0.0,
            stationarityConfidence: 1.0);

        Assert(result.State == MarketState.StructuralBreak, "The rule still reports structural break compatibility evidence.");
        AssertClose(0.64, result.Confidence, "High structural stability must penalize structural break compatibility.");
        Assert(result.Explanation.Contains("Scientific Score : 0.600"), "The penalized scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 0.640"), "The penalized final score must be shown.");
        Assert(result.Explanation.Contains("High Structural Stability"), "High structural stability must be described.");
        Assert(result.Explanation.Contains("Weak Persistence"), "Weak persistence must be described.");
        Assert(result.Explanation.Contains("Weak Mean Reversion"), "Weak mean reversion must be described.");
        Assert(result.Explanation.Contains("Weak Stationarity"), "Weak stationarity must be described.");
    }

    private static void AssertHighPersistencePenalty()
    {
        DecisionResult result = Evaluate(
            structuralStabilityValue: 0.0,
            structuralStabilityConfidence: 1.0,
            persistenceValue: 1.0,
            persistenceConfidence: 1.0,
            meanReversionValue: 0.0,
            meanReversionConfidence: 1.0,
            stationarityValue: 0.0,
            stationarityConfidence: 1.0);

        Assert(result.State == MarketState.StructuralBreak, "The rule still reports structural break compatibility evidence.");
        AssertClose(0.73, result.Confidence, "High persistence must reduce structural break compatibility.");
        Assert(result.Explanation.Contains("Scientific Score : 0.700"), "The reduced scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 0.730"), "The reduced final score must be shown.");
        Assert(result.Explanation.Contains("Low Structural Stability"), "Low structural stability must be described.");
        Assert(result.Explanation.Contains("Strong Persistence"), "Strong persistence must be described.");
        Assert(result.Explanation.Contains("Weak Mean Reversion"), "Weak mean reversion must be described.");
        Assert(result.Explanation.Contains("Weak Stationarity"), "Weak stationarity must be described.");
    }

    private static void AssertHighMeanReversionPenalty()
    {
        DecisionResult result = Evaluate(
            structuralStabilityValue: 0.0,
            structuralStabilityConfidence: 1.0,
            persistenceValue: 0.0,
            persistenceConfidence: 1.0,
            meanReversionValue: 1.0,
            meanReversionConfidence: 1.0,
            stationarityValue: 0.0,
            stationarityConfidence: 1.0);

        Assert(result.State == MarketState.StructuralBreak, "The rule still reports structural break compatibility evidence.");
        AssertClose(0.82, result.Confidence, "High mean reversion must reduce structural break compatibility.");
        Assert(result.Explanation.Contains("Scientific Score : 0.800"), "The reduced scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 0.820"), "The reduced final score must be shown.");
        Assert(result.Explanation.Contains("Low Structural Stability"), "Low structural stability must be described.");
        Assert(result.Explanation.Contains("Weak Persistence"), "Weak persistence must be described.");
        Assert(result.Explanation.Contains("Strong Mean Reversion"), "Strong mean reversion must be described.");
        Assert(result.Explanation.Contains("Weak Stationarity"), "Weak stationarity must be described.");
    }

    private static void AssertIncompleteFusionResult()
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.StructuralStability] = Confidence(value: 0.0, confidence: 1.0);
        builder.Dimensions[FusionDimension.Persistence] = Confidence(value: 0.0, confidence: 1.0);
        builder.Dimensions[FusionDimension.MeanReversion] = Confidence(value: 0.0, confidence: 1.0);

        DecisionResult result = Evaluate(builder.Build());

        Assert(result.State == MarketState.Unknown, "An incomplete fusion result must not select a state.");
        Assert(result.Confidence == 0.0, "An incomplete fusion result must not produce confidence.");
        Assert(result.TriggeredRules.Count == 0, "An incomplete fusion result must not trigger the rule.");
        Assert(result.RejectedRules.Contains(nameof(StructuralBreakRule)), "An incomplete fusion result must reject the rule.");
        Assert(result.Explanation == "No Decision Rule", "An incomplete fusion result must keep the default no decision explanation.");
    }

    private static DecisionResult Evaluate(
        double structuralStabilityValue,
        double structuralStabilityConfidence,
        double persistenceValue,
        double persistenceConfidence,
        double meanReversionValue,
        double meanReversionConfidence,
        double stationarityValue,
        double stationarityConfidence)
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.StructuralStability] = Confidence(
            structuralStabilityValue,
            structuralStabilityConfidence);
        builder.Dimensions[FusionDimension.Persistence] = Confidence(persistenceValue, persistenceConfidence);
        builder.Dimensions[FusionDimension.MeanReversion] = Confidence(meanReversionValue, meanReversionConfidence);
        builder.Dimensions[FusionDimension.Stationarity] = Confidence(stationarityValue, stationarityConfidence);

        return Evaluate(builder.Build());
    }

    private static DecisionResult Evaluate(FusionResult fusionResult)
    {
        var engine = new DecisionEngine([new StructuralBreakRule()]);
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
