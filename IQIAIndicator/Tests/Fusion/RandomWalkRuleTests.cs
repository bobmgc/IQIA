using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;

namespace IQIAIndicator.Tests.Fusion;

/// <summary>
/// Vérifications de contrat de la règle de marche aléatoire Variance Ratio.
/// </summary>
public static class RandomWalkRuleTests
{
    public static void RunAll()
    {
        AssertRandomWalkBehavior();
        AssertNonRandomWalkBehavior();
        AssertInsufficientEstimateQuality();
        AssertMissingEvidence();
    }

    private static void AssertRandomWalkBehavior()
    {
        FusionConfidence confidence = Evaluate(CreateVarianceRatio(1.0, 0.0, 0.95, 0.95, 200));
        Assert(confidence.Value > 0.8, "Une évidence compatible avec une marche aléatoire doit produire une confiance élevée.");
    }

    private static void AssertNonRandomWalkBehavior()
    {
        FusionConfidence confidence = Evaluate(CreateVarianceRatio(1.75, 4.0, 0.001, 0.95, 200));
        Assert(confidence.Value < 0.01, "Une évidence incompatible avec une marche aléatoire doit produire une confiance faible.");
    }

    private static void AssertInsufficientEstimateQuality()
    {
        FusionConfidence confidence = Evaluate(CreateVarianceRatio(1.0, 0.0, 0.95, 0.2, 10));
        Assert(confidence.Value < 0.1, "Une estimation peu fiable doit réduire la confiance.");
        Assert(confidence.Explanation.Contains("faible qualité de l'estimation"),
            "La faible qualité de l'estimation doit être expliquée.");
    }

    private static void AssertMissingEvidence()
    {
        FusionConfidence confidence = Evaluate(null);
        Assert(confidence.Value == 0.0, "Une évidence manquante doit produire une confiance nulle.");
        Assert(confidence.Explanation == "Missing Evidence", "L'évidence manquante doit être expliquée.");
    }

    private static FusionConfidence Evaluate(VarianceRatioResult? varianceRatio)
    {
        var builder = new FusionResultBuilder();
        new RandomWalkRule().Evaluate(
            new FusionContext
            {
                Evidence = new EvidenceSet
                {
                    Timestamp = DateTime.UnixEpoch,
                    Adf = null,
                    Kpss = null,
                    Hurst = null,
                    HalfLife = null,
                    VarianceRatio = varianceRatio,
                    Cusum = null,
                    Volatility = null,
                    BaiPerron = null,
                    Dfa = null
                },
                Timestamp = DateTime.UnixEpoch,
                Symbol = string.Empty,
                TimeFrame = string.Empty,
                EvaluationId = Guid.Empty
            },
            builder);

        return builder.Dimensions[FusionDimension.RandomWalk];
    }

    private static VarianceRatioResult CreateVarianceRatio(
        double ratio,
        double zStatistic,
        double pValue,
        double confidence,
        int sampleSize) => new()
    {
        VarianceRatio = ratio,
        ZStatistic = zStatistic,
        PValue = pValue,
        Confidence = confidence,
        Lag = 5,
        SampleSize = sampleSize,
        IsValid = true,
        Explanation = string.Empty
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}