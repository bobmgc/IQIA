using System.Linq;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.BaiPerron;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;

namespace IQIAIndicator.Tests.Fusion;

/// <summary>
/// Vérifications de contrat de la règle de stabilité structurelle CUSUM-Bai-Perron.
/// </summary>
public static class StructuralStabilityRuleTests
{
    public static void RunAll()
    {
        AssertStableStructure();
        AssertConfirmedBreak();
        AssertScientificDisagreement();
        AssertMissingEvidence();
    }

    private static void AssertStableStructure()
    {
        FusionConfidence confidence = Evaluate(
            CreateCusum(false, 0.0, 0.0, 1.0),
            CreateBaiPerron(0));

        Assert(confidence.Value > 0.8, "Des structures stables concordantes doivent produire une confiance élevée.");
        Assert(!confidence.Explanation.Contains("Scientific disagreement"), "L'accord ne doit pas être déclaré conflictuel.");
    }

    private static void AssertConfirmedBreak()
    {
        FusionConfidence confidence = Evaluate(
            CreateCusum(true, 2.0, 0.0, 1.0),
            CreateBaiPerron(2));

        Assert(confidence.Value < 0.2, "Des ruptures confirmées doivent produire une confiance faible.");
        Assert(!confidence.Explanation.Contains("Scientific disagreement"), "L'accord sur les ruptures ne doit pas être déclaré conflictuel.");
    }

    private static void AssertScientificDisagreement()
    {
        FusionConfidence confidence = Evaluate(
            CreateCusum(false, 0.0, 0.0, 1.0),
            CreateBaiPerron(2));

        Assert(confidence.Value > 0.25 && confidence.Value < 0.75,
            "Un désaccord scientifique doit produire une confiance intermédiaire.");
        Assert(confidence.Explanation.Contains("Scientific disagreement"), "Le désaccord doit être explicite.");
    }

    private static void AssertMissingEvidence()
    {
        FusionConfidence confidence = Evaluate(null, CreateBaiPerron(0));
        Assert(confidence.Value == 0.0, "Une évidence manquante doit produire une confiance faible.");
        Assert(confidence.Explanation == "Missing Evidence", "L'évidence manquante doit être expliquée.");
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