using System.Globalization;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Engine.Decision.Rules;

/// <summary>
/// Evaluates whether fused market evidence is compatible with a directional market.
/// </summary>
public sealed class TrendingRule : IDecisionRule
{
    // Provisional scientific weights for directional market compatibility.
    private const double PersistenceScientificWeight = 0.50;
    private const double StructuralStabilityScientificWeight = 0.30;
    private const double NonStationarityScientificWeight = 0.20;

    // Provisional final blend weights.
    private const double ScientificScoreWeight = 0.90;
    private const double QualityScoreWeight = 0.10;

    public void Evaluate(DecisionContext context, DecisionResultBuilder builder)
    {
        if (!TryReadRequiredDimensions(context.FusionResult, out DimensionScores scores))
        {
            builder.RejectedRules.Add(nameof(TrendingRule));
            return;
        }

        double scientificScore =
            PersistenceScientificWeight * scores.Persistence.Value +
            StructuralStabilityScientificWeight * scores.StructuralStability.Value +
            NonStationarityScientificWeight * (1.0 - scores.Stationarity.Value);
        double qualityScore =
            PersistenceScientificWeight * scores.Persistence.Confidence +
            StructuralStabilityScientificWeight * scores.StructuralStability.Confidence +
            NonStationarityScientificWeight * scores.Stationarity.Confidence;
        double finalScore = Math.Clamp(
            ScientificScoreWeight * scientificScore + QualityScoreWeight * qualityScore,
            0.0,
            1.0);

        if (finalScore > builder.Confidence)
        {
            builder.State = MarketState.Trending;
            builder.Confidence = finalScore;
            builder.Explanation = BuildExplanation(scientificScore, qualityScore, finalScore, scores);
            builder.TriggeredRules.Add(nameof(TrendingRule));
            return;
        }

        builder.RejectedRules.Add(nameof(TrendingRule));
    }

    private static bool TryReadRequiredDimensions(FusionResult fusionResult, out DimensionScores scores)
    {
        scores = default;

        if (!fusionResult.Dimensions.TryGetValue(FusionDimension.Persistence, out FusionConfidence? persistence) ||
            !fusionResult.Dimensions.TryGetValue(
                FusionDimension.StructuralStability,
                out FusionConfidence? structuralStability) ||
            !fusionResult.Dimensions.TryGetValue(FusionDimension.Stationarity, out FusionConfidence? stationarity))
        {
            return false;
        }

        scores = new DimensionScores(
            new DimensionScore(ClampScore(persistence.Value), ClampScore(persistence.Confidence)),
            new DimensionScore(ClampScore(structuralStability.Value), ClampScore(structuralStability.Confidence)),
            new DimensionScore(ClampScore(stationarity.Value), ClampScore(stationarity.Confidence)));

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
        $"{DescribePersistence(scores.Persistence.Value)}{Environment.NewLine}" +
        $"{DescribeStructuralStability(scores.StructuralStability.Value)}{Environment.NewLine}" +
        DescribeStationarity(scores.Stationarity.Value);

    private static string FormatScore(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

    private static string DescribePersistence(double value) => value switch
    {
        >= 0.75 => "Strong Persistence",
        >= 0.50 => "Moderate Persistence",
        _ => "Weak Persistence"
    };

    private static string DescribeStructuralStability(double value) => value switch
    {
        >= 0.75 => "High Structural Stability",
        >= 0.50 => "Moderate Structural Stability",
        _ => "Low Structural Stability"
    };

    private static string DescribeStationarity(double value) => value switch
    {
        >= 0.75 => "High Stationarity",
        >= 0.50 => "Moderate Stationarity",
        _ => "Low Stationarity"
    };

    private readonly record struct DimensionScore(double Value, double Confidence);

    private readonly record struct DimensionScores(
        DimensionScore Persistence,
        DimensionScore StructuralStability,
        DimensionScore Stationarity);
}
