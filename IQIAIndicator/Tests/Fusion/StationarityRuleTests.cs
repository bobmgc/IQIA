using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.ADF;
using IQIAIndicator.Engine.Regime.Evidence.KPSS;

namespace IQIAIndicator.Tests.Fusion;

/// <summary>
/// Vérifications de contrat de la règle de stationnarité ADF-KPSS.
/// </summary>
public static class StationarityRuleTests
{
    public static void RunAll()
    {
        AssertPositiveAgreement();
        AssertNegativeAgreement();
        AssertDisagreement();
        AssertMissingEvidence();
    }

    private static void AssertPositiveAgreement()
    {
        FusionConfidence confidence = Evaluate(CreateAdf(true, 0.01m), CreateKpss(true, 0.09m));
        Assert(confidence.Value > 0.5, "L'accord positif doit soutenir la stationnarité.");
        Assert(confidence.Explanation.Contains("agreement: stationarity"), "L'accord positif doit être expliqué.");
    }

    private static void AssertNegativeAgreement()
    {
        FusionConfidence confidence = Evaluate(CreateAdf(false, 0.90m), CreateKpss(false, 0.01m));
        Assert(confidence.Value < 0.5, "L'accord négatif doit affaiblir la stationnarité.");
        Assert(confidence.Explanation.Contains("agreement: non-stationarity"), "L'accord négatif doit être expliqué.");
    }

    private static void AssertDisagreement()
    {
        FusionConfidence confidence = Evaluate(CreateAdf(true, 0.01m), CreateKpss(false, 0.01m));
        Assert(confidence.Value >= 0.25 && confidence.Value <= 0.75,
            "Le désaccord doit conserver une confiance intermédiaire.");
        Assert(confidence.Explanation.Contains("disagreement"), "Le désaccord doit être explicite.");
    }

    private static void AssertMissingEvidence()
    {
        FusionConfidence confidence = Evaluate(null, CreateKpss(true, 0.09m));
        Assert(confidence.Value == 0.0, "Une évidence manquante doit produire une confiance faible.");
        Assert(confidence.Explanation == "Missing Evidence", "L'évidence manquante doit être expliquée.");
    }

    private static FusionConfidence Evaluate(AdfResult? adf, KpssResult? kpss)
    {
        var builder = new FusionResultBuilder();
        new StationarityRule().Evaluate(
            new FusionContext
            {
                Evidence = new EvidenceSet
                {
                    Timestamp = DateTime.UnixEpoch,
                    Adf = adf,
                    Kpss = kpss,
                    Hurst = null,
                    HalfLife = null,
                    VarianceRatio = null,
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

        return builder.Dimensions[FusionDimension.Stationarity];
    }

    private static AdfResult CreateAdf(bool isStationary, decimal pValue) => new()
    {
        Statistic = 0m,
        PValue = pValue,
        Confidence = 1m,
        CriticalValue1 = 0m,
        CriticalValue5 = 0m,
        CriticalValue10 = 0m,
        IsStationary = isStationary,
        LagUsed = 0,
        SampleSize = 100,
        Explanation = string.Empty,
        IsValid = true
    };

    private static KpssResult CreateKpss(bool isStationary, decimal pValue) => new()
    {
        Statistic = 0m,
        PValue = pValue,
        Confidence = 1m,
        CriticalValue1 = 0m,
        CriticalValue5 = 0m,
        CriticalValue10 = 0m,
        IsStationary = isStationary,
        Bandwidth = 0,
        SampleSize = 100,
        Explanation = string.Empty,
        IsValid = true
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}