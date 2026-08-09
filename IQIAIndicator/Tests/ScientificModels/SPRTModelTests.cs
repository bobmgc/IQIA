using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.Validation;

namespace IQIAIndicator.Tests.ScientificModels;

public static class SPRTModelTests
{
    public static void RunAll()
    {
        AssertEvidenceFavorsH1();
        AssertEvidenceFavorsH0();
        AssertContinueZone();
        AssertPerEvidenceLogLikelihoodMatchesLLR();
        AssertWaldBoundsAreRespected();
        AssertScientificResultsAbsentIsRejected();
        AssertNaNAndInfinityAreHandled();
        AssertMetricsAndDiagnosticsPresent();
    }

    private static void AssertEvidenceFavorsH1()
    {
        var context = CreateContext(
            dynamicZScore: 3.0,
            dynamicConfidence: 0.9,
            halfLife: 2.0,
            meanReversionStrength: 0.95,
            relativeVolatility: 0.9,
            currentVolatility: 0.5,
            volatilityConfidence: 0.95,
            volatilityRegime: "LOW");

        var result = new SPRTModel().Evaluate(context);

        Assert(result.Success, "Valid scientific evidence should produce a successful SPRT result.");
        Assert(result.Metrics is not null, "Metrics must be present.");
        Assert(result.Metrics![("SPRTDecision")] is string decision && decision == "ACCEPT_H1", "Strong evidence must accept H1.");
        Assert(result.Metrics![("LikelihoodRatio")] is double likelihoodRatio && likelihoodRatio > 1.0, "LikelihoodRatio must favor H1.");
        Assert(result.Metrics![("LogLikelihoodRatio")] is double logLikelihoodRatio && logLikelihoodRatio > 0.0, "LogLikelihoodRatio must be positive for H1.");
    }

    private static void AssertEvidenceFavorsH0()
    {
        var context = CreateContext(
            dynamicZScore: 0.1,
            dynamicConfidence: 0.1,
            halfLife: 50.0,
            meanReversionStrength: 0.1,
            relativeVolatility: 2.0,
            currentVolatility: 3.0,
            volatilityConfidence: 0.1,
            volatilityRegime: "HIGH");

        var result = new SPRTModel().Evaluate(context);

        Assert(result.Success, "Valid scientific evidence should produce a successful SPRT result.");
        Assert(result.Metrics is not null, "Metrics must be present.");
        Assert(result.Metrics![("SPRTDecision")] is string decision && decision == "ACCEPT_H0", "Weak evidence must accept H0.");
        Assert(result.Metrics![("LikelihoodRatio")] is double likelihoodRatio && likelihoodRatio < 1.0, "LikelihoodRatio must favor H0.");
        Assert(result.Metrics![("LogLikelihoodRatio")] is double logLikelihoodRatio && logLikelihoodRatio < 0.0, "LogLikelihoodRatio must be negative for H0.");
    }

    private static void AssertContinueZone()
    {
        var context = CreateContext(
            dynamicZScore: 0.6,
            dynamicConfidence: 0.6,
            halfLife: 10.0,
            meanReversionStrength: 0.5,
            relativeVolatility: 1.0,
            currentVolatility: 1.0,
            volatilityConfidence: 0.5,
            volatilityRegime: "MEDIUM");

        var result = new SPRTModel().Evaluate(context);

        Assert(result.Success, "Valid scientific evidence should produce a successful SPRT result.");
        Assert(result.Metrics is not null, "Metrics must be present.");
        Assert(result.Metrics![("SPRTDecision")] is string decision && decision == "CONTINUE", "Ambiguous evidence must continue.");
    }

    private static void AssertPerEvidenceLogLikelihoodMatchesLLR()
    {
        var context = CreateContext(
            dynamicZScore: 1.5,
            dynamicConfidence: 0.8,
            halfLife: 3.0,
            meanReversionStrength: 0.85,
            relativeVolatility: 0.95,
            currentVolatility: 0.7,
            volatilityConfidence: 0.9,
            volatilityRegime: "LOW");

        var result = new SPRTModel().Evaluate(context);
        Assert(result.Success, "Valid scientific evidence should produce a successful SPRT result.");
        Assert(result.Metrics is not null, "Metrics must be present.");

        var metrics = result.Metrics!;
        if (!(metrics["PerEvidenceLogLikelihood"] is Dictionary<string, double> logLikelihoods))
        {
            throw new InvalidOperationException("PerEvidenceLogLikelihood must be a dictionary.");
        }

        if (!(metrics["LogLikelihoodRatio"] is double logLikelihoodRatio))
        {
            throw new InvalidOperationException("LogLikelihoodRatio must be a double.");
        }

        double sum = 0.0;
        foreach (var value in logLikelihoods.Values)
        {
            Assert(double.IsFinite(value), "Each per-evidence log-likelihood must be finite.");
            sum += value;
        }

        Assert(Math.Abs(sum - logLikelihoodRatio) < 1e-9,
            "LogLikelihoodRatio must equal the sum of per-evidence log-likelihoods.");
    }

    private static void AssertWaldBoundsAreRespected()
    {
        var contextH1 = CreateContext(
            dynamicZScore: 4.0,
            dynamicConfidence: 0.95,
            halfLife: 1.0,
            meanReversionStrength: 0.98,
            relativeVolatility: 0.8,
            currentVolatility: 0.4,
            volatilityConfidence: 0.95,
            volatilityRegime: "LOW");

        var resultH1 = new SPRTModel().Evaluate(contextH1);
        Assert(resultH1.Metrics is not null, "Metrics must be present.");
        Assert(resultH1.Metrics![("SPRTDecision")] is string decisionH1 && decisionH1 == "ACCEPT_H1",
            "Strong evidence should accept H1.");

        var contextH0 = CreateContext(
            dynamicZScore: 0.1,
            dynamicConfidence: 0.05,
            halfLife: 80.0,
            meanReversionStrength: 0.05,
            relativeVolatility: 2.0,
            currentVolatility: 2.5,
            volatilityConfidence: 0.05,
            volatilityRegime: "HIGH");

        var resultH0 = new SPRTModel().Evaluate(contextH0);
        Assert(resultH0.Metrics is not null, "Metrics must be present.");
        Assert(resultH0.Metrics![("SPRTDecision")] is string decisionH0 && decisionH0 == "ACCEPT_H0",
            "Weak evidence should accept H0.");
    }

    private static void AssertScientificResultsAbsentIsRejected()
    {
        var context = new ScientificModelContext(
            new MarketContext(DateTime.UtcNow, 100m, new decimal[] { 100m, 101m, 102m, 103m }),
            new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.9 },
            new MethodologySelection(
                new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.9 },
                new QuantitativeMethodology(
                    "MeanReversionMethodology",
                    "Mean Reversion Methodology",
                    "SPRTModel",
                    new[] { "KalmanFilterModel", "OrnsteinUhlenbeckModel", "DynamicZScoreModel", "VolatilityModel" },
                    "SPRT",
                    new[] { "MeanReverting" },
                    "1.0",
                    Array.Empty<string>()),
                DateTime.UtcNow,
                "1.0",
                "Mean Reversion selected."),
            null);

        var result = new SPRTModel().Evaluate(context);

        Assert(!result.Success, "Missing ScientificResults must be rejected.");
        Assert(result.Score == 0.0, "Rejected result must have score 0.");
        Assert(result.Metrics is not null, "Metrics must still be present.");
    }

    private static void AssertNaNAndInfinityAreHandled()
    {
        var contextNaN = CreateContext(
            dynamicZScore: double.NaN,
            dynamicConfidence: 0.8,
            halfLife: 4.0,
            meanReversionStrength: 0.9,
            relativeVolatility: 0.8,
            currentVolatility: 0.4,
            volatilityConfidence: 0.9,
            volatilityRegime: "LOW");

        var resultNaN = new SPRTModel().Evaluate(contextNaN);
        Assert(!resultNaN.Success, "NaN prior metric should be rejected.");

        var contextInf = CreateContext(
            dynamicZScore: 1.2,
            dynamicConfidence: double.PositiveInfinity,
            halfLife: 4.0,
            meanReversionStrength: 0.9,
            relativeVolatility: 0.8,
            currentVolatility: 0.4,
            volatilityConfidence: 0.9,
            volatilityRegime: "LOW");

        var resultInf = new SPRTModel().Evaluate(contextInf);
        Assert(!resultInf.Success, "Infinity prior metric should be rejected.");
    }

    private static void AssertMetricsAndDiagnosticsPresent()
    {
        var context = CreateContext(
            dynamicZScore: 1.2,
            dynamicConfidence: 0.8,
            halfLife: 5.0,
            meanReversionStrength: 0.8,
            relativeVolatility: 1.0,
            currentVolatility: 0.5,
            volatilityConfidence: 0.8,
            volatilityRegime: "MEDIUM");

        var result = new SPRTModel().Evaluate(context);

        Assert(result.Metrics is not null, "Metrics must be present.");
        var metrics = result.Metrics!;
        Assert(metrics.ContainsKey("LikelihoodRatio"), "LikelihoodRatio must be present.");
        Assert(metrics.ContainsKey("LogLikelihoodRatio"), "LogLikelihoodRatio must be present.");
        Assert(metrics.ContainsKey("SPRTDecision"), "SPRTDecision must be present.");
        Assert(metrics.ContainsKey("DecisionStrength"), "DecisionStrength must be present.");
        Assert(metrics.ContainsKey("EvidenceStrength"), "EvidenceStrength must be present.");
        Assert(metrics.ContainsKey("SPRTConfidence"), "SPRTConfidence must be present.");
        Assert(metrics.ContainsKey("Diagnostics"), "Diagnostics must be present.");
    }

    private static ScientificModelContext CreateContext(
        double dynamicZScore,
        double dynamicConfidence,
        double halfLife,
        double meanReversionStrength,
        double relativeVolatility,
        double currentVolatility,
        double volatilityConfidence,
        string volatilityRegime)
    {
        var dynamicZScoreMetrics = new Dictionary<string, object>
        {
            ["DynamicZScore"] = dynamicZScore,
            ["DynamicConfidence"] = dynamicConfidence,
            ["Diagnostics"] = "Synthetic DynamicZScore metrics."
        };

        var ouMetrics = new Dictionary<string, object>
        {
            ["HalfLife"] = halfLife,
            ["MeanReversionStrength"] = meanReversionStrength,
            ["Diagnostics"] = "Synthetic Ornstein-Uhlenbeck metrics."
        };

        var volatilityMetrics = new Dictionary<string, object>
        {
            ["RelativeVolatility"] = relativeVolatility,
            ["CurrentVolatility"] = currentVolatility,
            ["VolatilityConfidence"] = volatilityConfidence,
            ["VolatilityRegime"] = volatilityRegime,
            ["Diagnostics"] = "Synthetic Volatility metrics."
        };

        var scientificResults = new[]
        {
            new ScientificModelResult("DynamicZScoreModel", true, 1.0, "Synthetic DynamicZScore result.", dynamicZScoreMetrics),
            new ScientificModelResult("OrnsteinUhlenbeckModel", true, 1.0, "Synthetic OU result.", ouMetrics),
            new ScientificModelResult("VolatilityModel", true, 1.0, "Synthetic Volatility result.", volatilityMetrics)
        };

        var methodologySelection = new MethodologySelection(
            new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.9 },
            new QuantitativeMethodology(
                "MeanReversionMethodology",
                "Mean Reversion Methodology",
                "SPRTModel",
                new[] { "KalmanFilterModel", "OrnsteinUhlenbeckModel", "DynamicZScoreModel", "VolatilityModel" },
                "SPRT",
                new[] { "MeanReverting" },
                "1.0",
                Array.Empty<string>()),
            DateTime.UtcNow,
            "1.0",
            "Mean Reversion selected.");

        return new ScientificModelContext(
            new MarketContext(DateTime.UtcNow, 100m, new decimal[] { 100m, 101m, 102m, 103m, 104m }),
            new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.9 },
            methodologySelection,
            scientificResults);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
