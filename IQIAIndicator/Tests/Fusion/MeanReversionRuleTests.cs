using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.HalfLife;

namespace IQIAIndicator.Tests.Fusion;

/// <summary>
/// Vérifications de contrat de la règle de retour à la moyenne Half-Life.
/// </summary>
public static class MeanReversionRuleTests
{
    public static void RunAll()
    {
        AssertShortHalfLife();
        AssertLongHalfLife();
        AssertInsufficientEstimateQuality();
        AssertMissingEvidence();
    }

    private static void AssertShortHalfLife()
    {
        FusionConfidence confidence = Evaluate(CreateHalfLife(1.0, 0.95, 0.95, 200));
        Assert(confidence.Value > 0.7, "Une demi-vie courte avec une bonne qualité doit produire une confiance élevée.");
    }

    private static void AssertLongHalfLife()
    {
        FusionConfidence confidence = Evaluate(CreateHalfLife(100.0, 0.95, 0.95, 200));
        Assert(confidence.Value < 0.01, "Une demi-vie longue doit produire une confiance faible.");
    }

    private static void AssertInsufficientEstimateQuality()
    {
        FusionConfidence confidence = Evaluate(CreateHalfLife(1.0, 0.2, 0.2, 10));
        Assert(confidence.Value < 0.1, "Une estimation peu fiable doit réduire la confiance.");
        Assert(confidence.Explanation.Contains("qualité de l'estimation est insuffisante"),
            "La qualité insuffisante doit être expliquée.");
    }

    private static void AssertMissingEvidence()
    {
        FusionConfidence confidence = Evaluate(null);
        Assert(confidence.Value == 0.0, "Une évidence manquante doit produire une confiance faible.");
        Assert(confidence.Explanation == "Missing Evidence", "L'évidence manquante doit être expliquée.");
    }

    private static FusionConfidence Evaluate(HalfLifeResult? halfLife)
    {
        var builder = new FusionResultBuilder();
        new MeanReversionRule().Evaluate(
            new FusionContext
            {
                Evidence = new EvidenceSet
                {
                    Timestamp = DateTime.UnixEpoch,
                    Adf = null,
                    Kpss = null,
                    Hurst = null,
                    HalfLife = halfLife,
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

        return builder.Dimensions[FusionDimension.MeanReversion];
    }

    private static HalfLifeResult CreateHalfLife(
        double halfLife,
        double confidence,
        double rSquared,
        int sampleSize) => new()
    {
        HalfLife = halfLife,
        Lambda = -0.1,
        Intercept = 0.0,
        StandardError = 0.1,
        RSquared = rSquared,
        Confidence = confidence,
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