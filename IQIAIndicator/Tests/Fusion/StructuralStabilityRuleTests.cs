using System.Linq;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Profile;
using IQIAIndicator.Engine.Fusion.Rules;

namespace IQIAIndicator.Tests.Fusion;

/// <summary>
/// Contract checks for current structural coherence from CUSUM and Bai-Perron.
/// </summary>
public static class StructuralStabilityRuleTests
{
    public static void RunAll()
    {
        AssertStableStructure();
        AssertConfirmedBreak();
        AssertRecoveryAfterHistoricalBreaks();
        AssertMissingEvidence();
    }

    private static void AssertStableStructure()
    {
        FusionConfidence confidence = Evaluate(
            behaviourConsistency: 0.98,
            profileVelocity: 0.05,
            profileStability: 0.95);

        Assert(confidence.Value > 0.95, "A coherent current structure must produce high structural stability.");
        Assert(confidence.Explanation.Contains("BehaviourConsistency=") &&
            confidence.Explanation.Contains("ProfileStability=") &&
            confidence.Explanation.Contains("ProfileVelocity="),
            "Current structural profile must be explained.");
    }

    private static void AssertConfirmedBreak()
    {
        FusionConfidence confidence = Evaluate(
            behaviourConsistency: 0.20,
            profileVelocity: 0.90,
            profileStability: 0.12);

        Assert(confidence.Value < 0.35, "Strong transition pressure must quickly reduce structural stability.");
        Assert(confidence.Explanation.Contains("BehaviourConsistency=") &&
            confidence.Explanation.Contains("ProfileStability=") &&
            confidence.Explanation.Contains("ProfileVelocity="),
            "Structural transition profile must be explained.");
    }

    private static void AssertRecoveryAfterHistoricalBreaks()
    {
        FusionConfidence confidence = Evaluate(
            behaviourConsistency: 0.98,
            profileVelocity: 0.05,
            profileStability: 0.95);

        Assert(confidence.Value > 0.75,
            "Recovered current coherence must not stay near zero because of historical breaks.");
        Assert(confidence.Explanation.Contains("BehaviourConsistency=") &&
            confidence.Explanation.Contains("ProfileStability=") &&
            confidence.Explanation.Contains("ProfileVelocity="),
            "Recovered structural profile must be explained.");
    }

    private static void AssertMissingEvidence()
    {
        FusionConfidence confidence = Evaluate(new FusionProfileAnalysis
        {
            SnapshotCount = 1,
            WindowSize = 6,
            BehaviourConsistency = 0.0,
            ProfileVelocity = 0.0,
            ProfileStability = 0.0
        });

        Assert(confidence.Value == 1.0, "Profile warm-up must use the stable default value.");
        Assert(confidence.Confidence == 1.0, "Profile warm-up must use full confidence.");
        Assert(confidence.Explanation ==
            "Warm-up: insufficient profile history to evaluate structural stability.",
            "Profile warm-up must be explained.");
    }

    private static FusionConfidence Evaluate(
        double behaviourConsistency,
        double profileVelocity,
        double profileStability) =>
        Evaluate(new FusionProfileAnalysis
        {
            SnapshotCount = 4,
            WindowSize = 6,
            BehaviourConsistency = behaviourConsistency,
            ProfileVelocity = profileVelocity,
            ProfileStability = profileStability
        });

    private static FusionConfidence Evaluate(FusionProfileAnalysis profileAnalysis)
    {
        return new StructuralStabilityRule().EvaluateAnalysis(profileAnalysis);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
