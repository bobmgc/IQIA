using System.Globalization;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Engine.Decision.Rules;

/// <summary>
/// Evaluates whether fused market evidence is compatible with a stable range.
/// </summary>
public sealed class StableRangeRule : IDecisionRule
{
    // Provisional scientific weights for stable range compatibility.
    private const double StationarityScientificWeight = 0.40;
    private const double MeanReversionScientificWeight = 0.40;
    private const double StructuralStabilityScientificWeight = 0.20;

    // Provisional final blend weights.
    private const double ScientificScoreWeight = 0.90;
    private const double QualityScoreWeight = 0.10;

    public void Evaluate(DecisionContext context, DecisionResultBuilder builder)
    {
        if (!TryReadRequiredDimensions(context.FusionResult, out DimensionScores scores))
        {
            builder.RejectedRules.Add(nameof(StableRangeRule));
            return;
        }

        double scientificScore =
            StationarityScientificWeight * scores.Stationarity.Value +
            MeanReversionScientificWeight * scores.MeanReversion.Value +
            StructuralStabilityScientificWeight * scores.StructuralStability.Value;
        double qualityScore =
            StationarityScientificWeight * scores.Stationarity.Confidence +
            MeanReversionScientificWeight * scores.MeanReversion.Confidence +
            StructuralStabilityScientificWeight * scores.StructuralStability.Confidence;
        double finalScore = Math.Clamp(
            ScientificScoreWeight * scientificScore + QualityScoreWeight * qualityScore,
            0.0,
            1.0);

        if (finalScore > builder.Confidence)
        {
            builder.State = MarketState.StableRange;
            builder.Confidence = finalScore;
            builder.Explanation = BuildExplanation(scientificScore, qualityScore, finalScore, scores);
            builder.TriggeredRules.Add(nameof(StableRangeRule));
            return;
        }

        builder.RejectedRules.Add(nameof(StableRangeRule));
    }

    private static bool TryReadRequiredDimensions(FusionResult fusionResult, out DimensionScores scores)
    {
        scores = default;

        if (!fusionResult.Dimensions.TryGetValue(FusionDimension.Stationarity, out FusionConfidence? stationarity) ||
            !fusionResult.Dimensions.TryGetValue(FusionDimension.MeanReversion, out FusionConfidence? meanReversion) ||
            !fusionResult.Dimensions.TryGetValue(
                FusionDimension.StructuralStability,
                out FusionConfidence? structuralStability))
        {
            return false;
        }

        scores = new DimensionScores(
            new DimensionScore(EffectiveValue(stationarity), ClampScore(stationarity.Confidence)),
            new DimensionScore(EffectiveValue(meanReversion), ClampScore(meanReversion.Confidence)),
            new DimensionScore(EffectiveValue(structuralStability), ClampScore(structuralStability.Confidence)));

        return true;
    }

    private static double ClampScore(double value) => Math.Clamp(value, 0.0, 1.0);

    // Missing evidence (FusionConfidence.IsAvailable == false) must never read as directional
    // information. A raw Value of 0.0 on an unavailable dimension means "unknown", not "measured
    // zero" - using it directly (or via a (1 - Value) inversion elsewhere in this file's callers)
    // would fabricate a signal from an absence of data (Sprint 14 / audit findings DEC-01, FUS-02).
    // The neutral midpoint of the [0,1] range is used instead, so an unavailable dimension
    // contributes neither for nor against any regime this rule scores. Weights are unchanged.
    private const double NeutralMissingEvidenceValue = 0.5;

    private static double EffectiveValue(FusionConfidence confidence) =>
        confidence.IsAvailable ? ClampScore(confidence.Value) : NeutralMissingEvidenceValue;

    private static string BuildExplanation(
        double scientificScore,
        double qualityScore,
        double finalScore,
        DimensionScores scores) =>
        $"Scientific Score : {FormatScore(scientificScore)}{Environment.NewLine}" +
        $"Quality Score : {FormatScore(qualityScore)}{Environment.NewLine}" +
        $"Final Score : {FormatScore(finalScore)}{Environment.NewLine}" +
        $"Decision Confidence : {FormatScore(qualityScore)}{Environment.NewLine}" +
        $"{DescribeStationarity(scores.Stationarity.Value)}{Environment.NewLine}" +
        $"{DescribeMeanReversion(scores.MeanReversion.Value)}{Environment.NewLine}" +
        DescribeStructuralStability(scores.StructuralStability.Value);

    private static string FormatScore(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

    private static string DescribeStationarity(double value) => value switch
    {
        >= 0.75 => "Strong Stationarity",
        >= 0.50 => "Moderate Stationarity",
        _ => "Weak Stationarity"
    };

    private static string DescribeMeanReversion(double value) => value switch
    {
        >= 0.75 => "Strong Mean Reversion",
        >= 0.50 => "Moderate Mean Reversion",
        _ => "Weak Mean Reversion"
    };

    private static string DescribeStructuralStability(double value) => value switch
    {
        >= 0.75 => "High Structural Stability",
        >= 0.50 => "Moderate Structural Stability",
        _ => "Low Structural Stability"
    };

    private readonly record struct DimensionScore(double Value, double Confidence);

    private readonly record struct DimensionScores(
        DimensionScore Stationarity,
        DimensionScore MeanReversion,
        DimensionScore StructuralStability);
}
