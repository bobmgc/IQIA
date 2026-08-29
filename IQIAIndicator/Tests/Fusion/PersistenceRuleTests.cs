using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.DFA;
using IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;

namespace IQIAIndicator.Tests.Fusion;

/// <summary>
/// Vérifications de contrat de la règle de persistance DFA-Variance Ratio.
/// </summary>
public static class PersistenceRuleTests
{
    public static void RunAll()
    {
        AssertStrongPersistence();
        AssertRandomWalkBehavior();
        AssertScientificDisagreement();
        AssertMissingEvidence();
    }

    private static void AssertStrongPersistence()
    {
        FusionConfidence confidence = Evaluate(CreateDfa(0.95), CreateVarianceRatio(2.5, 4.0, 0.001));
        Assert(confidence.Value > 0.5, "Les évidences persistantes concordantes doivent produire une confiance élevée.");
        Assert(!confidence.Explanation.Contains("Scientific disagreement"), "L'accord ne doit pas être déclaré conflictuel.");
    }

    private static void AssertRandomWalkBehavior()
    {
        FusionConfidence confidence = Evaluate(CreateDfa(0.5), CreateVarianceRatio(1.0, 0.0, 0.5));
        Assert(confidence.Value < 0.1, "Des évidences proches du hasard doivent produire une confiance faible.");
    }

    private static void AssertScientificDisagreement()
    {
        FusionConfidence confidence = Evaluate(CreateDfa(0.95), CreateVarianceRatio(0.4, -4.0, 0.001));
        Assert(confidence.Value > 0.25 && confidence.Value < 0.75,
            "Un désaccord scientifique doit produire une confiance intermédiaire.");
        Assert(confidence.Explanation.Contains("Scientific disagreement"), "Le désaccord doit être explicite.");
    }

    private static void AssertMissingEvidence()
    {
        FusionConfidence confidence = Evaluate(null, CreateVarianceRatio(2.5, 4.0, 0.001));
        Assert(confidence.Value == 0.0, "Une évidence manquante doit produire une confiance faible.");
        Assert(confidence.Explanation == "Missing Evidence", "L'évidence manquante doit être expliquée.");
    }

    private static FusionConfidence Evaluate(DfaResult? dfa, VarianceRatioResult? varianceRatio)
    {
        var builder = new FusionResultBuilder();
        new PersistenceRule().Evaluate(
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
                    Dfa = dfa
                },
                Timestamp = DateTime.UnixEpoch,
                Symbol = string.Empty,
                TimeFrame = string.Empty,
                EvaluationId = Guid.Empty
            },
            builder);

        return builder.Dimensions[FusionDimension.Persistence];
    }

    private static DfaResult CreateDfa(double hurst) => new()
    {
        Hurst = hurst,
        RSquared = 0.95,
        Confidence = 0.95,
        WindowCount = 10,
        IsValid = true,
        Explanation = string.Empty
    };

    private static VarianceRatioResult CreateVarianceRatio(double ratio, double zStatistic, double pValue) => new()
    {
        VarianceRatio = ratio,
        ZStatistic = zStatistic,
        PValue = pValue,
        Confidence = 0.99,
        Lag = 5,
        SampleSize = 100,
        IsValid = true,
        Explanation = string.Empty
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}