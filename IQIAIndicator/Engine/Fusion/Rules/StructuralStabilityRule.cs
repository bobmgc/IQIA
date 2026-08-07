using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Profile;

namespace IQIAIndicator.Engine.Fusion.Rules;

/// <summary>
/// Produces Structural Stability from the temporal evolution of the fusion profile.
/// </summary>
public sealed class StructuralStabilityRule
{
    public string Name => nameof(StructuralStabilityRule);

    public FusionConfidence EvaluateAnalysis(FusionProfileAnalysis analysis)
    {
        if (analysis.SnapshotCount < 2)
        {
            return new FusionConfidence
            {
                Value = 1.0,
                Confidence = 1.0,
                Explanation = "Warm-up: insufficient profile history to evaluate structural stability."
            };
        }

        double value = Math.Clamp(analysis.BehaviourConsistency, 0.0, 1.0);
        double confidence = Math.Clamp(
            0.50 * analysis.ProfileStability +
            0.50 * (1.0 - analysis.ProfileVelocity),
            0.0,
            1.0);

        return new FusionConfidence
        {
            Value = value,
            Confidence = confidence,
            Explanation = StructuralStabilityExplanation(analysis)
        };
    }

    private static string StructuralStabilityExplanation(FusionProfileAnalysis analysis) =>
        $"BehaviourConsistency={analysis.BehaviourConsistency:F3}; " +
        $"ProfileStability={analysis.ProfileStability:F3}; " +
        $"ProfileVelocity={analysis.ProfileVelocity:F3}.";
}
