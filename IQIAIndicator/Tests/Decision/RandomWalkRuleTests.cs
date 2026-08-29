using System.Linq;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.Rules;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Tests.Decision;

/// <summary>
/// Contract checks for random walk compatibility from fused dimensions only.
/// </summary>
public static class RandomWalkRuleTests
{
    public static void RunAll()
    {
        AssertStrongRandomWalk();
        AssertHighPersistencePenalty();
        AssertHighMeanReversionPenalty();
        AssertWeakRandomWalk();
        AssertIncompleteFusionResult();
    }

    private static void AssertStrongRandomWalk()
    {
        DecisionResult result = Evaluate(
            randomWalkValue: 1.0,
            randomWalkConfidence: 1.0,
            persistenceValue: 0.0,
            persistenceConfidence: 1.0,
            meanReversionValue: 0.0,
            meanReversionConfidence: 1.0);

        Assert(result.State == MarketState.RandomWalk, "Strong random walk evidence must select the random walk state.");
        AssertClose(1.0, result.Confidence, "Strong random walk evidence must produce a high final score.");
        Assert(result.TriggeredRules.Contains(nameof(RandomWalkRule)), "The random walk rule must be triggered.");
        Assert(result.RejectedRules.Count == 0, "A selected random walk rule must not be rejected.");
        Assert(result.Explanation.Contains("Scientific Score : 1.000"), "The scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Decision Confidence : 1.000"), "Decision confidence must match quality score.");
        Assert(result.Explanation.Contains("Final Score : 1.000"), "The final score must be shown.");
        Assert(result.Explanation.Contains("High Random Walk Compatibility"), "High random walk compatibility must be described.");
        Assert(result.Explanation.Contains("Weak Persistence"), "Weak persistence must be described.");
        Assert(result.Explanation.Contains("Weak Mean Reversion"), "Weak mean reversion must be described.");
        Assert(result.Explanation.Contains("Market behaves close to a stochastic process"), "The stochastic process hypothesis must be explicit.");
    }

    private static void AssertHighPersistencePenalty()
    {
        DecisionResult result = Evaluate(
            randomWalkValue: 1.0,
            randomWalkConfidence: 1.0,
            persistenceValue: 1.0,
            persistenceConfidence: 1.0,
            meanReversionValue: 0.0,
            meanReversionConfidence: 1.0);

        Assert(result.State == MarketState.RandomWalk, "The rule still reports random walk compatibility evidence.");
        AssertClose(0.82, result.Confidence, "High persistence must penalize random walk compatibility.");
        Assert(result.Explanation.Contains("Scientific Score : 0.800"), "The penalized scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 0.820"), "The penalized final score must be shown.");
        Assert(result.Explanation.Contains("High Random Walk Compatibility"), "Random walk compatibility must remain high.");
        Assert(result.Explanation.Contains("Strong Persistence"), "Strong persistence must be described.");
        Assert(result.Explanation.Contains("Weak Mean Reversion"), "Weak mean reversion must be described.");
    }

    private static void AssertHighMeanReversionPenalty()
    {
        DecisionResult result = Evaluate(
            randomWalkValue: 1.0,
            randomWalkConfidence: 1.0,
            persistenceValue: 0.0,
            persistenceConfidence: 1.0,
            meanReversionValue: 1.0,
            meanReversionConfidence: 1.0);

        Assert(result.State == MarketState.RandomWalk, "The rule still reports random walk compatibility evidence.");
        AssertClose(0.82, result.Confidence, "High mean reversion must penalize random walk compatibility.");
        Assert(result.Explanation.Contains("Scientific Score : 0.800"), "The penalized scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 0.820"), "The penalized final score must be shown.");
        Assert(result.Explanation.Contains("High Random Walk Compatibility"), "Random walk compatibility must remain high.");
        Assert(result.Explanation.Contains("Weak Persistence"), "Weak persistence must be described.");
        Assert(result.Explanation.Contains("Strong Mean Reversion"), "Strong mean reversion must be described.");
    }

    private static void AssertWeakRandomWalk()
    {
        DecisionResult result = Evaluate(
            randomWalkValue: 0.0,
            randomWalkConfidence: 1.0,
            persistenceValue: 0.0,
            persistenceConfidence: 1.0,
            meanReversionValue: 0.0,
            meanReversionConfidence: 1.0);

        Assert(result.State == MarketState.RandomWalk, "The rule still reports random walk compatibility evidence.");
        AssertClose(0.46, result.Confidence, "Weak random walk evidence must reduce the final score.");
        Assert(result.Explanation.Contains("Scientific Score : 0.400"), "The reduced scientific score must be shown.");
        Assert(result.Explanation.Contains("Quality Score : 1.000"), "The quality score must be shown.");
        Assert(result.Explanation.Contains("Final Score : 0.460"), "The reduced final score must be shown.");
        Assert(result.Explanation.Contains("Low Random Walk Compatibility"), "Low random walk compatibility must be described.");
        Assert(result.Explanation.Contains("Weak Persistence"), "Weak persistence must be described.");
        Assert(result.Explanation.Contains("Weak Mean Reversion"), "Weak mean reversion must be described.");
    }

    private static void AssertIncompleteFusionResult()
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.RandomWalk] = Confidence(value: 1.0, confidence: 1.0);
        builder.Dimensions[FusionDimension.Persistence] = Confidence(value: 0.0, confidence: 1.0);

        DecisionResult result = Evaluate(builder.Build());

        Assert(result.State == MarketState.Unknown, "An incomplete fusion result must not select a state.");
        Assert(result.Confidence == 0.0, "An incomplete fusion result must not produce confidence.");
        Assert(result.TriggeredRules.Count == 0, "An incomplete fusion result must not trigger the rule.");
        Assert(result.RejectedRules.Contains(nameof(RandomWalkRule)), "An incomplete fusion result must reject the rule.");
        Assert(result.Explanation == "No Decision Rule", "An incomplete fusion result must keep the default no decision explanation.");
    }

    private static DecisionResult Evaluate(
        double randomWalkValue,
        double randomWalkConfidence,
        double persistenceValue,
        double persistenceConfidence,
        double meanReversionValue,
        double meanReversionConfidence)
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.RandomWalk] = Confidence(randomWalkValue, randomWalkConfidence);
        builder.Dimensions[FusionDimension.Persistence] = Confidence(persistenceValue, persistenceConfidence);
        builder.Dimensions[FusionDimension.MeanReversion] = Confidence(meanReversionValue, meanReversionConfidence);

        return Evaluate(builder.Build());
    }

    private static DecisionResult Evaluate(FusionResult fusionResult)
    {
        var engine = new DecisionEngine([new RandomWalkRule()]);
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
