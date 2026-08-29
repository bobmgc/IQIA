using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificModels.Validation;

public sealed class SPRTModel : IScientificModel
{
    public string Name => "SPRTModel";

    public string Category => "Validation";

    private const double DefaultAlpha = 0.05;
    private const double DefaultBeta = 0.05;
    private const double MinimumProbability = 1e-9;
    private const double MaximumLogLikelihoodPerEvidence = 6.0;

    public ScientificModelResult Evaluate(ScientificModelContext context)
    {
        bool compatible =
            context.DecisionResult.Winner == MarketState.MeanReverting &&
            context.MethodologySelection.SelectedMethodology.Name == "MeanReversionMethodology";

        if (!compatible)
        {
            return FailureResult(
                "The selected methodology and decision winner are not compatible with the Mean Reversion scientific stack.",
                "SPRTModel requires MeanReversionMethodology and MeanReverting market state.");
        }

        if (context.ScientificResults is null || context.ScientificResults.Count == 0)
        {
            return FailureResult(
                "SPRTModel requires prior ScientificResults from the scientific pipeline.",
                "ScientificResults is missing or empty.");
        }

        if (!TryGetPriorMetrics(context.ScientificResults, out var priorMetrics, out var explanation))
        {
            return FailureResult(
                "SPRTModel requires a complete set of prior scientific metrics to compute a likelihood ratio.",
                explanation);
        }

        var perEvidenceLogLikelihood = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var perEvidenceLikelihood = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        AddEvidence(perEvidenceLogLikelihood, perEvidenceLikelihood, "DynamicZScore", ComputeDynamicZScoreContribution((double)priorMetrics["DynamicZScore"]));
        AddEvidence(perEvidenceLogLikelihood, perEvidenceLikelihood, "MeanReversionStrength", ComputeMeanReversionStrengthContribution((double)priorMetrics["MeanReversionStrength"]));
        AddEvidence(perEvidenceLogLikelihood, perEvidenceLikelihood, "RelativeVolatility", ComputeRelativeVolatilityContribution((double)priorMetrics["RelativeVolatility"]));
        AddEvidence(perEvidenceLogLikelihood, perEvidenceLikelihood, "CurrentVolatility", ComputeCurrentVolatilityContribution((double)priorMetrics["CurrentVolatility"]));
        AddEvidence(perEvidenceLogLikelihood, perEvidenceLikelihood, "HalfLife", ComputeHalfLifeContribution((double)priorMetrics["HalfLife"]));
        AddEvidence(perEvidenceLogLikelihood, perEvidenceLikelihood, "DynamicConfidence", ComputeConfidenceContribution((double)priorMetrics["DynamicConfidence"]));
        AddEvidence(perEvidenceLogLikelihood, perEvidenceLikelihood, "VolatilityConfidence", ComputeConfidenceContribution((double)priorMetrics["VolatilityConfidence"]));

        double logLikelihoodRatio = perEvidenceLogLikelihood.Values.Sum();
        double likelihoodRatio = Math.Exp(Math.Clamp(logLikelihoodRatio, -700.0, 700.0));

        if (!double.IsFinite(logLikelihoodRatio) || !double.IsFinite(likelihoodRatio))
        {
            return FailureResult(
                "SPRTModel produced an invalid numeric result during likelihood computation.",
                $"logLikelihoodRatio={logLikelihoodRatio}, likelihoodRatio={likelihoodRatio}");
        }

        double upperBound = Math.Log((1.0 - DefaultBeta) / DefaultAlpha);
        double lowerBound = Math.Log(DefaultBeta / (1.0 - DefaultAlpha));
        string decision = DetermineDecision(logLikelihoodRatio, upperBound, lowerBound);
        double decisionStrength = ComputeDecisionStrength(logLikelihoodRatio, upperBound, lowerBound);
        double evidenceStrength = ComputeEvidenceStrength(perEvidenceLogLikelihood);
        double sprtConfidence = ComputeSPRTConfidence(evidenceStrength, decisionStrength);

        string diagnostics =
            "SPRT evaluation completed. " +
            $"LogLikelihoodRatio={logLikelihoodRatio:F6}; " +
            $"LikelihoodRatio={likelihoodRatio:F6}; " +
            $"EvidenceStrength={evidenceStrength:F6}; " +
            $"DecisionStrength={decisionStrength:F6}; " +
            $"SPRTConfidence={sprtConfidence:F6}; " +
            $"Alpha={DefaultAlpha:F2}; Beta={DefaultBeta:F2}; " +
            $"UpperBound={upperBound:F6}; LowerBound={lowerBound:F6}.";

        var metrics = new Dictionary<string, object>(priorMetrics)
        {
            ["LikelihoodRatio"] = likelihoodRatio,
            ["LogLikelihoodRatio"] = logLikelihoodRatio,
            ["SPRTDecision"] = decision,
            ["DecisionStrength"] = decisionStrength,
            ["EvidenceStrength"] = evidenceStrength,
            ["SPRTConfidence"] = sprtConfidence,
            ["Alpha"] = DefaultAlpha,
            ["Beta"] = DefaultBeta,
            ["UpperBound"] = upperBound,
            ["LowerBound"] = lowerBound,
            ["PerEvidenceLikelihood"] = perEvidenceLikelihood,
            ["PerEvidenceLogLikelihood"] = perEvidenceLogLikelihood,
            ["Diagnostics"] = diagnostics
        };

        return new ScientificModelResult(
            Name,
            true,
            sprtConfidence,
            "SPRTModel completed sequential evidence validation.",
            metrics);
    }

    private static ScientificModelResult FailureResult(string explanation, string diagnostics)
    {
        var metrics = new Dictionary<string, object>
        {
            ["LikelihoodRatio"] = 1.0,
            ["LogLikelihoodRatio"] = 0.0,
            ["SPRTDecision"] = "CONTINUE",
            ["DecisionStrength"] = 0.0,
            ["EvidenceStrength"] = 0.0,
            ["SPRTConfidence"] = 0.0,
            ["Alpha"] = DefaultAlpha,
            ["Beta"] = DefaultBeta,
            ["UpperBound"] = Math.Log((1.0 - DefaultBeta) / DefaultAlpha),
            ["LowerBound"] = Math.Log(DefaultBeta / (1.0 - DefaultAlpha)),
            ["PerEvidenceLikelihood"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
            ["PerEvidenceLogLikelihood"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
            ["Diagnostics"] = diagnostics
        };

        return new ScientificModelResult(
            "SPRTModel",
            false,
            0.0,
            explanation,
            metrics);
    }

    private static bool TryGetPriorMetrics(IReadOnlyList<ScientificModelResult> scientificResults, out Dictionary<string, object> metrics, out string explanation)
    {
        metrics = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        explanation = string.Empty;

        ScientificModelResult? dynamicZScoreResult = scientificResults.FirstOrDefault(result => result.ModelName == "DynamicZScoreModel");
        ScientificModelResult? ouResult = scientificResults.FirstOrDefault(result => result.ModelName == "OrnsteinUhlenbeckModel");
        ScientificModelResult? volatilityResult = scientificResults.FirstOrDefault(result => result.ModelName == "VolatilityModel");

        if (dynamicZScoreResult is null || !dynamicZScoreResult.Success || dynamicZScoreResult.Metrics is null)
        {
            explanation = "SPRTModel requires valid DynamicZScoreModel metrics from prior scientific execution.";
            return false;
        }

        if (ouResult is null || !ouResult.Success || ouResult.Metrics is null)
        {
            explanation = "SPRTModel requires valid OrnsteinUhlenbeckModel metrics from prior scientific execution.";
            return false;
        }

        if (volatilityResult is null || !volatilityResult.Success || volatilityResult.Metrics is null)
        {
            explanation = "SPRTModel requires valid VolatilityModel metrics from prior scientific execution.";
            return false;
        }

        if (!TryGetFiniteDouble(dynamicZScoreResult.Metrics, "DynamicZScore", out double dynamicZScore) ||
            !TryGetFiniteDouble(dynamicZScoreResult.Metrics, "DynamicConfidence", out double dynamicConfidence))
        {
            explanation = "SPRTModel requires finite DynamicZScore and DynamicConfidence from DynamicZScoreModel.";
            return false;
        }

        if (!TryGetFiniteDouble(ouResult.Metrics, "HalfLife", out double halfLife) ||
            !TryGetFiniteDouble(ouResult.Metrics, "MeanReversionStrength", out double meanReversionStrength))
        {
            explanation = "SPRTModel requires finite HalfLife and MeanReversionStrength from OrnsteinUhlenbeckModel.";
            return false;
        }

        if (!TryGetFiniteDouble(volatilityResult.Metrics, "RelativeVolatility", out double relativeVolatility) ||
            !TryGetFiniteDouble(volatilityResult.Metrics, "CurrentVolatility", out double currentVolatility) ||
            !TryGetFiniteDouble(volatilityResult.Metrics, "VolatilityConfidence", out double volatilityConfidence) ||
            !TryGetString(volatilityResult.Metrics, "VolatilityRegime", out string volatilityRegime))
        {
            explanation = "SPRTModel requires finite RelativeVolatility, CurrentVolatility, VolatilityConfidence and VolatilityRegime from VolatilityModel.";
            return false;
        }

        metrics["DynamicZScore"] = dynamicZScore;
        metrics["DynamicConfidence"] = dynamicConfidence;
        metrics["HalfLife"] = halfLife;
        metrics["MeanReversionStrength"] = meanReversionStrength;
        metrics["RelativeVolatility"] = relativeVolatility;
        metrics["CurrentVolatility"] = currentVolatility;
        metrics["VolatilityConfidence"] = volatilityConfidence;
        metrics["VolatilityRegime"] = volatilityRegime;

        return true;
    }

    private static void AddEvidence(Dictionary<string, double> perEvidenceLogLikelihood, Dictionary<string, double> perEvidenceLikelihood, string key, (double logLikelihood, double ratio) evidence)
    {
        perEvidenceLogLikelihood[key] = evidence.logLikelihood;
        perEvidenceLikelihood[key] = evidence.ratio;
    }

    private static double ComputeEvidenceStrength(IReadOnlyDictionary<string, double> perEvidenceLogLikelihood)
    {
        double totalAbsolute = perEvidenceLogLikelihood.Values.Sum(Math.Abs);
        double maximumTotal = MaximumLogLikelihoodPerEvidence * perEvidenceLogLikelihood.Count;
        return Math.Clamp(totalAbsolute / Math.Max(maximumTotal, MinimumProbability), 0.0, 1.0);
    }

    private static string DetermineDecision(double logLikelihoodRatio, double upperBound, double lowerBound)
    {
        if (logLikelihoodRatio >= upperBound)
        {
            return "ACCEPT_H1";
        }

        if (logLikelihoodRatio <= lowerBound)
        {
            return "ACCEPT_H0";
        }

        return "CONTINUE";
    }

    private static double ComputeDecisionStrength(double logLikelihoodRatio, double upperBound, double lowerBound)
    {
        double halfRange = Math.Abs(upperBound - lowerBound) / 2.0;
        if (halfRange <= 0.0)
        {
            return 0.0;
        }

        double distanceToNearestBound = Math.Min(Math.Abs(upperBound - logLikelihoodRatio), Math.Abs(logLikelihoodRatio - lowerBound));
        return Math.Clamp(1.0 - distanceToNearestBound / halfRange, 0.0, 1.0);
    }

    private static double ComputeSPRTConfidence(double evidenceStrength, double decisionStrength)
    {
        return Math.Clamp(evidenceStrength * decisionStrength, 0.0, 1.0);
    }

    private static (double logLikelihood, double ratio) ComputeDynamicZScoreContribution(double dynamicZScore)
    {
        double x = Math.Abs(dynamicZScore);
        double pH1 = GaussianDensity(x, 2.0, 1.0);
        double pH0 = GaussianDensity(x, 0.0, 1.0);
        return BuildLikelihood(pH1, pH0);
    }

    private static (double logLikelihood, double ratio) ComputeMeanReversionStrengthContribution(double meanReversionStrength)
    {
        double x = Math.Clamp(meanReversionStrength, 0.0, 1.0);
        double pH1 = Math.Clamp(0.2 + 0.8 * x, MinimumProbability, 1.0);
        double pH0 = Math.Clamp(0.8 - 0.6 * x, MinimumProbability, 1.0);
        return BuildLikelihood(pH1, pH0);
    }

    private static (double logLikelihood, double ratio) ComputeRelativeVolatilityContribution(double relativeVolatility)
    {
        double x = Math.Max(relativeVolatility, 0.0);
        double pH1 = GaussianDensity(x, 1.0, 0.4);
        double pH0 = GaussianDensity(x, 1.5, 0.6);
        return BuildLikelihood(pH1, pH0);
    }

    private static (double logLikelihood, double ratio) ComputeCurrentVolatilityContribution(double currentVolatility)
    {
        double x = Math.Max(currentVolatility, 0.0);
        double pH1 = ExponentialDensity(x, 1.0);
        double pH0 = ExponentialDensity(x, 0.5);
        return BuildLikelihood(pH1, pH0);
    }

    private static (double logLikelihood, double ratio) ComputeHalfLifeContribution(double halfLife)
    {
        double x = Math.Max(halfLife, 0.0);
        double pH1 = ExponentialDensity(x, 0.5);
        double pH0 = ExponentialDensity(x, 0.1);
        return BuildLikelihood(pH1, pH0);
    }

    private static (double logLikelihood, double ratio) ComputeConfidenceContribution(double confidence)
    {
        double x = Math.Clamp(confidence, 0.0, 1.0);
        double pH1 = Math.Clamp(0.1 + 0.8 * x, MinimumProbability, 1.0);
        double pH0 = Math.Clamp(0.9 - 0.8 * x, MinimumProbability, 1.0);
        return BuildLikelihood(pH1, pH0);
    }

    private static (double logLikelihood, double ratio) BuildLikelihood(double pH1, double pH0)
    {
        pH1 = Math.Clamp(pH1, MinimumProbability, 1.0);
        pH0 = Math.Clamp(pH0, MinimumProbability, 1.0);
        double ratio = pH1 / pH0;
        return (Math.Log(ratio), ratio);
    }

    private static double GaussianDensity(double x, double mean, double sigma)
    {
        if (!double.IsFinite(x) || sigma <= 0.0)
        {
            return MinimumProbability;
        }

        double diff = x - mean;
        double exponent = -0.5 * (diff * diff) / (sigma * sigma);
        return Math.Max(MinimumProbability, Math.Exp(exponent) / (sigma * Math.Sqrt(2.0 * Math.PI)));
    }

    private static double ExponentialDensity(double x, double lambda)
    {
        if (!double.IsFinite(x) || !double.IsFinite(lambda) || lambda <= 0.0)
        {
            return MinimumProbability;
        }

        return Math.Max(MinimumProbability, lambda * Math.Exp(-lambda * x));
    }

    private static bool TryGetFiniteDouble(IReadOnlyDictionary<string, object> metrics, string key, out double value)
    {
        if (metrics.TryGetValue(key, out object? raw) && raw is double typed && double.IsFinite(typed))
        {
            value = typed;
            return true;
        }

        value = 0.0;
        return false;
    }

    private static bool TryGetString(IReadOnlyDictionary<string, object> metrics, string key, out string value)
    {
        if (metrics.TryGetValue(key, out object? raw) && raw is string text && !string.IsNullOrWhiteSpace(text))
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
