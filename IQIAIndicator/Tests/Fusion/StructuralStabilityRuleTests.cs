using System.Linq;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.BaiPerron;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;

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
            CreateCusum(false, 0.0, 0.0, 1.0),
            CreateBaiPerron(0));

        Assert(confidence.Value > 0.95, "A coherent current structure must produce high structural stability.");
        Assert(confidence.Explanation.Contains("Current structural coherence"), "Current coherence must be explained.");
    }

    private static void AssertConfirmedBreak()
    {
        FusionConfidence confidence = Evaluate(
            CreateCusum(true, 2.0, 0.0, 1.0),
            CreateBaiPerron(2));

        Assert(confidence.Value < 0.35, "Strong transition pressure must quickly reduce structural stability.");
        Assert(confidence.Explanation.Contains("transition pressure"), "Transition pressure must be explained.");
    }

    private static void AssertRecoveryAfterHistoricalBreaks()
    {
        FusionConfidence confidence = Evaluate(
            CreateCusum(false, 0.0, 0.0, 1.0),
            CreateBaiPerron(6));

        Assert(confidence.Value > 0.75,
            "Recovered current coherence must not stay near zero because of historical breaks.");
        Assert(confidence.Explanation.Contains("recovery"), "Structural recovery must be explained.");
    }

    private static void AssertMissingEvidence()
    {
        FusionConfidence confidence = Evaluate(null, CreateBaiPerron(0));
        Assert(confidence.Value == 0.0, "Missing evidence must produce low structural stability.");
        Assert(confidence.Confidence == 0.0, "Missing evidence must produce low quality.");
        Assert(confidence.Explanation == "Missing Evidence", "Missing evidence must be explained.");
    }

    private static FusionConfidence Evaluate(CusumResult? cusum, BaiPerronResult? baiPerron)
    {
        var builder = new FusionResultBuilder();
        new StructuralStabilityRule().Evaluate(
            new FusionContext
            {
                Evidence = new EvidenceSet
                {
                    Timestamp = DateTime.UnixEpoch,
                    Adf = null,
                    Kpss = null,
                    Hurst = null,
                    HalfLife = null,
                    VarianceRatio = null,
                    Cusum = cusum,
                    Volatility = null,
                    BaiPerron = baiPerron,
                    Dfa = null
                },
                Timestamp = DateTime.UnixEpoch,
                Symbol = string.Empty,
                TimeFrame = string.Empty,
                EvaluationId = Guid.Empty
            },
            builder);

        return builder.Dimensions[FusionDimension.StructuralStability];
    }

    private static CusumResult CreateCusum(
        bool changeDetected,
        double positiveCusum,
        double negativeCusum,
        double threshold) => new()
    {
        ChangeDetected = changeDetected,
        EstimatedBreakIndex = changeDetected ? 50 : -1,
        PositiveCusum = positiveCusum,
        NegativeCusum = negativeCusum,
        Threshold = threshold,
        Confidence = 0.95,
        SampleSize = 200,
        IsValid = true,
        Explanation = string.Empty
    };

    private static BaiPerronResult CreateBaiPerron(int breakCount) => new()
    {
        Breakpoints = Enumerable.Range(1, breakCount).Select(index => index * 50).ToArray(),
        BreakCount = breakCount,
        Confidence = 0.95,
        GlobalRSS = 1.0,
        BicScore = 1.0,
        SampleSize = 200,
        IsValid = true,
        Explanation = string.Empty
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
