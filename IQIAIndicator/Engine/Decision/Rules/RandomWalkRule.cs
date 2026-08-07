using System.Globalization;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Engine.Decision.Rules;

/// <summary>
/// Evaluates whether fused market evidence is compatible with a random walk.
/// </summary>
public sealed class RandomWalkRule : IDecisionRule
{
    // Provisional scientific weights for random walk compatibility.
    private const double RandomWalkScientificWeight = 0.60;
    private const double NonPersistenceScientificWeight = 0.20;
    private const double NonMeanReversionScientificWeight = 0.20;

    // Provisional final blend weights.
    private const double ScientificScoreWeight = 0.90;
    private const double QualityScoreWeight = 0.10;

    public void Evaluate(DecisionContext context, DecisionResultBuilder builder)
    {
        if (!TryReadRequiredDimensions(context.FusionResult, out DimensionScores scores))
        {
            builder.RejectedRules.Add(nameof(RandomWalkRule));
            return;
        }

        double scientificScore =
            RandomWalkScientificWeight * scores.RandomWalk.Value +
            NonPersistenceScientificWeight * (1.0 - scores.Persistence.Value) +
            NonMeanReversionScientificWeight * (1.0 - scores.MeanReversion.Value);
        double qualityScore =
            RandomWalkScientificWeight * scores.RandomWalk.Confidence +
            NonPersistenceScientificWeight * scores.Persistence.Confidence +
            NonMeanReversionScientificWeight * scores.MeanReversion.Confidence;
        double finalScore = Math.Clamp(
            ScientificScoreWeight * scientificScore + QualityScoreWeight * qualityScore,
            0.0,
            1.0);

        if (finalScore > builder.Confidence)
        {
            builder.State = MarketState.RandomWalk;
            builder.Confidence = finalScore;
            builder.Explanation = BuildExplanation(scientificScore, qualityScore, finalScore, scores);
            builder.TriggeredRules.Add(nameof(RandomWalkRule));
            return;
        }

        builder.RejectedRules.Add(nameof(RandomWalkRule));
    }

    private static bool TryReadRequiredDimensions(FusionResult fusionResult, out DimensionScores scores)
    {
        scores = default;

        if (!fusionResult.Dimensions.TryGetValue(FusionDimension.RandomWalk, out FusionConfidence? randomWalk) ||
            !fusionResult.Dimensions.TryGetValue(FusionDimension.Persistence, out FusionConfidence? persistence) ||
            !fusionResult.Dimensions.TryGetValue(FusionDimension.MeanReversion, out FusionConfidence? meanReversion))
        {
            return false;
        }

        scores = new DimensionScores(
            new DimensionScore(ClampScore(randomWalk.Value), ClampScore(randomWalk.Confidence)),
            new DimensionScore(ClampScore(persistence.Value), ClampScore(persistence.Confidence)),
            new DimensionScore(ClampScore(meanReversion.Value), ClampScore(meanReversion.Confidence)));

        return true;
    }

    private static double ClampScore(double value) => Math.Clamp(value, 0.0, 1.0);

    private static string BuildExplanation(
        double scientificScore,
        double qualityScore,
        double finalScore,
        DimensionScores scores) =>
        $"Scientific Score : {FormatScore(scientificScore)}{Environment.NewLine}" +
        $"Quality Score : {FormatScore(qualityScore)}{Environment.NewLine}" +
        $"Decision Confidence : {FormatScore(qualityScore)}{Environment.NewLine}" +
        $"Final Score : {FormatScore(finalScore)}{Environment.NewLine}" +
        $"{DescribeRandomWalk(scores.RandomWalk.Value)}{Environment.NewLine}" +
        $"{DescribePersistence(scores.Persistence.Value)}{Environment.NewLine}" +
        $"{DescribeMeanReversion(scores.MeanReversion.Value)}{Environment.NewLine}" +
        "Market behaves close to a stochastic process";

    private static string FormatScore(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

    private static string DescribeRandomWalk(double value) => value switch
    {
        >= 0.75 => "High Random Walk Compatibility",
        >= 0.50 => "Moderate Random Walk Compatibility",
        _ => "Low Random Walk Compatibility"
    };

    private static string DescribePersistence(double value) => value switch
    {
        >= 0.75 => "Strong Persistence",
        >= 0.50 => "Moderate Persistence",
        _ => "Weak Persistence"
    };

    private static string DescribeMeanReversion(double value) => value switch
    {
        >= 0.75 => "Strong Mean Reversion",
        >= 0.50 => "Moderate Mean Reversion",
        _ => "Weak Mean Reversion"
    };

    private readonly record struct DimensionScore(double Value, double Confidence);

    private readonly record struct DimensionScores(
        DimensionScore RandomWalk,
        DimensionScore Persistence,
        DimensionScore MeanReversion);
}
