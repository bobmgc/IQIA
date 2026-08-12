using System.Globalization;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Engine.Decision.Rules;

/// <summary>
/// Evaluates whether fused market evidence is compatible with a mean reverting regime.
/// </summary>
public sealed class MeanRevertingRule : IDecisionRule
{
    // Provisional scientific weights for mean reversion compatibility.
    private const double MeanReversionScientificWeight = 0.40;
    private const double StationarityScientificWeight = 0.30;
    private const double NonPersistenceScientificWeight = 0.20;
    private const double StructuralStabilityScientificWeight = 0.10;

    // Provisional final blend weights.
    private const double ScientificScoreWeight = 0.90;
    private const double QualityScoreWeight = 0.10;

    public void Evaluate(DecisionContext context, DecisionResultBuilder builder)
    {
        if (!TryReadRequiredDimensions(context.FusionResult, out DimensionScores scores))
        {
            builder.RejectedRules.Add(nameof(MeanRevertingRule));
            return;
        }

        double scientificScore =
            MeanReversionScientificWeight * scores.MeanReversion.Value +
            StationarityScientificWeight * scores.Stationarity.Value +
            NonPersistenceScientificWeight * (1.0 - scores.Persistence.Value) +
            StructuralStabilityScientificWeight * scores.StructuralStability.Value;
        double qualityScore =
            MeanReversionScientificWeight * scores.MeanReversion.Confidence +
            StationarityScientificWeight * scores.Stationarity.Confidence +
            NonPersistenceScientificWeight * scores.Persistence.Confidence +
            StructuralStabilityScientificWeight * scores.StructuralStability.Confidence;
        double finalScore = Math.Clamp(
            ScientificScoreWeight * scientificScore + QualityScoreWeight * qualityScore,
            0.0,
            1.0);

        if (finalScore > builder.Confidence)
        {
            builder.State = MarketState.MeanReverting;
            builder.Confidence = finalScore;
            builder.Explanation = BuildExplanation(scientificScore, qualityScore, finalScore, scores);
            builder.TriggeredRules.Add(nameof(MeanRevertingRule));
            return;
        }

        builder.RejectedRules.Add(nameof(MeanRevertingRule));
    }

    private static bool TryReadRequiredDimensions(FusionResult fusionResult, out DimensionScores scores)
    {
        scores = default;

        if (!fusionResult.Dimensions.TryGetValue(FusionDimension.MeanReversion, out FusionConfidence? meanReversion) ||
            !fusionResult.Dimensions.TryGetValue(FusionDimension.Stationarity, out FusionConfidence? stationarity) ||
            !fusionResult.Dimensions.TryGetValue(FusionDimension.Persistence, out FusionConfidence? persistence) ||
            !fusionResult.Dimensions.TryGetValue(
                FusionDimension.StructuralStability,
                out FusionConfidence? structuralStability))
        {
            return false;
        }

        scores = new DimensionScores(
            new DimensionScore(EffectiveValue(meanReversion), ClampScore(meanReversion.Confidence)),
            new DimensionScore(EffectiveValue(stationarity), ClampScore(stationarity.Confidence)),
            new DimensionScore(EffectiveValue(persistence), ClampScore(persistence.Confidence)),
            new DimensionScore(EffectiveValue(structuralStability), ClampScore(structuralStability.Confidence)));

        return true;
    }

    private static double ClampScore(double value) => Math.Clamp(value, 0.0, 1.0);

    // See StableRangeRule.cs for the full rationale (Sprint 14 / DEC-01, FUS-02): missing evidence
    // must contribute neutrally, never as fabricated directional support - including through this
    // rule's (1 - Persistence.Value) inversion term.
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
        $"Decision Confidence : {FormatScore(qualityScore)}{Environment.NewLine}" +
        $"Final Score : {FormatScore(finalScore)}{Environment.NewLine}" +
        $"{DescribeMeanReversion(scores.MeanReversion.Value)}{Environment.NewLine}" +
        $"{DescribeStationarity(scores.Stationarity.Value)}{Environment.NewLine}" +
        $"{DescribePersistence(scores.Persistence.Value)}{Environment.NewLine}" +
        DescribeStructuralStability(scores.StructuralStability.Value);

    private static string FormatScore(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

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

    private static string DescribePersistence(double value) => value switch
    {
        >= 0.75 => "High Persistence",
        >= 0.50 => "Moderate Persistence",
        _ => "Low Persistence"
    };

    private static string DescribeStructuralStability(double value) => value switch
    {
        >= 0.75 => "High Structural Stability",
        >= 0.50 => "Moderate Structural Stability",
        _ => "Low Structural Stability"
    };

    private readonly record struct DimensionScore(double Value, double Confidence);

    private readonly record struct DimensionScores(
        DimensionScore MeanReversion,
        DimensionScore Stationarity,
        DimensionScore Persistence,
        DimensionScore StructuralStability);
}
