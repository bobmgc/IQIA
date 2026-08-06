using System.Globalization;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Engine.Decision.Rules;

/// <summary>
/// Evaluates whether fused market evidence is compatible with a structural break.
/// </summary>
public sealed class StructuralBreakRule : IDecisionRule
{
    // Provisional scientific weights for structural break compatibility.
    private const double StructuralInstabilityScientificWeight = 0.40;
    private const double NonPersistenceScientificWeight = 0.30;
    private const double NonMeanReversionScientificWeight = 0.20;
    private const double NonStationarityScientificWeight = 0.10;

    // Provisional final blend weights.
    private const double ScientificScoreWeight = 0.90;
    private const double QualityScoreWeight = 0.10;

    public void Evaluate(DecisionContext context, DecisionResultBuilder builder)
    {
        if (!TryReadRequiredDimensions(context.FusionResult, out DimensionScores scores))
        {
            builder.RejectedRules.Add(nameof(StructuralBreakRule));
            return;
        }

        double scientificScore =
            StructuralInstabilityScientificWeight * (1.0 - scores.StructuralStability.Value) +
            NonPersistenceScientificWeight * (1.0 - scores.Persistence.Value) +
            NonMeanReversionScientificWeight * (1.0 - scores.MeanReversion.Value) +
            NonStationarityScientificWeight * (1.0 - scores.Stationarity.Value);
        double qualityScore =
            StructuralInstabilityScientificWeight * scores.StructuralStability.Confidence +
            NonPersistenceScientificWeight * scores.Persistence.Confidence +
            NonMeanReversionScientificWeight * scores.MeanReversion.Confidence +
            NonStationarityScientificWeight * scores.Stationarity.Confidence;
        double finalScore = Math.Clamp(
            ScientificScoreWeight * scientificScore + QualityScoreWeight * qualityScore,
            0.0,
            1.0);
        string explanation = BuildExplanation(scientificScore, qualityScore, finalScore, scores);

        builder.SetCandidate(MarketState.StructuralBreak, scientificScore, qualityScore, finalScore, explanation);

        if (finalScore > builder.Confidence)
        {
            builder.State = MarketState.StructuralBreak;
            builder.Confidence = finalScore;
            builder.Explanation = explanation;
            builder.TriggeredRules.Add(nameof(StructuralBreakRule));
            return;
        }

        builder.RejectedRules.Add(nameof(StructuralBreakRule));
    }

    private static bool TryReadRequiredDimensions(FusionResult fusionResult, out DimensionScores scores)
    {
        scores = default;

        if (!fusionResult.Dimensions.TryGetValue(
                FusionDimension.StructuralStability,
                out FusionConfidence? structuralStability) ||
            !fusionResult.Dimensions.TryGetValue(FusionDimension.Persistence, out FusionConfidence? persistence) ||
            !fusionResult.Dimensions.TryGetValue(FusionDimension.MeanReversion, out FusionConfidence? meanReversion) ||
            !fusionResult.Dimensions.TryGetValue(FusionDimension.Stationarity, out FusionConfidence? stationarity))
        {
            return false;
        }

        scores = new DimensionScores(
            new DimensionScore(ClampScore(structuralStability.Value), ClampScore(structuralStability.Confidence)),
            new DimensionScore(ClampScore(persistence.Value), ClampScore(persistence.Confidence)),
            new DimensionScore(ClampScore(meanReversion.Value), ClampScore(meanReversion.Confidence)),
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
        $"{DescribeStructuralStability(scores.StructuralStability.Value)}{Environment.NewLine}" +
        $"{DescribePersistence(scores.Persistence.Value)}{Environment.NewLine}" +
        $"{DescribeMeanReversion(scores.MeanReversion.Value)}{Environment.NewLine}" +
        $"{DescribeStationarity(scores.Stationarity.Value)}{Environment.NewLine}" +
        "Possible Regime Transition";

    private static string FormatScore(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

    private static string DescribeStructuralStability(double value) => value switch
    {
        >= 0.75 => "High Structural Stability",
        >= 0.50 => "Moderate Structural Stability",
        _ => "Low Structural Stability"
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

    private static string DescribeStationarity(double value) => value switch
    {
        >= 0.75 => "Strong Stationarity",
        >= 0.50 => "Moderate Stationarity",
        _ => "Weak Stationarity"
    };

    private readonly record struct DimensionScore(double Value, double Confidence);

    private readonly record struct DimensionScores(
        DimensionScore StructuralStability,
        DimensionScore Persistence,
        DimensionScore MeanReversion,
        DimensionScore Stationarity);
}
