using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.MeanReversion;

namespace IQIAIndicator.Tests.ScientificModels;

public static class OrnsteinUhlenbeckModelTests
{
    public static void RunAll()
    {
        AssertConstantSeriesProducesStrongReversion();
        AssertStationarySeriesProducesPositiveTheta();
        AssertLargeShockDrivesThetaAndStrengthToZero();
        AssertRandomSeriesRemainsNumericallyStable();
        AssertOutputMetricsAreKalmanCompatible();
    }

    private static void AssertConstantSeriesProducesStrongReversion()
    {
        var context = CreateContext(new decimal[] { 100m, 100m, 100m, 100m, 100m }, 100m);
        var result = new OrnsteinUhlenbeckModel().Evaluate(context);

        Assert(result.Success, "Constant series must return a successful result.");
        Assert(result.Score >= 0.95, "Constant series must produce a strong OU score.");
        if (result.Metrics is null)
            throw new InvalidOperationException("Metrics must be present.");

        if (!result.Metrics.TryGetValue(ScientificMetricKeys.EstimatedTheta, out var estimatedThetaValueObj) || estimatedThetaValueObj is not double estimatedThetaValue)
            throw new InvalidOperationException("Metrics must contain EstimatedTheta as a double.");
        if (!result.Metrics.TryGetValue("HalfLife", out var halfLifeValueObj) || halfLifeValueObj is not double halfLifeValue)
            throw new InvalidOperationException("Metrics must contain HalfLife as a double.");
        if (!result.Metrics.TryGetValue("MeanReversionStrength", out var strengthValueObj) || strengthValueObj is not double strengthValue)
            throw new InvalidOperationException("Metrics must contain MeanReversionStrength as a double.");
        Assert(halfLifeValue < 10.0, "Half-Life must be short for a constant series.");
    }

    private static void AssertStationarySeriesProducesPositiveTheta()
    {
        var history = new decimal[] { 100m, 101m, 99m, 100m, 101m, 100m };
        var context = CreateContext(history, 100m);
        var result = new OrnsteinUhlenbeckModel().Evaluate(context);

        Assert(result.Success, "Stationary series must return a successful result.");
        if (result.Metrics is null)
            throw new InvalidOperationException("Metrics must be present.");

        if (!result.Metrics.TryGetValue("EstimatedTheta", out var thetaValueObj) || thetaValueObj is not double thetaValue)
            throw new InvalidOperationException("Metrics must contain EstimatedTheta as a double.");
        Assert(thetaValue > 0.0, "Stationary series must produce positive theta.");
        Assert(result.Explanation.Contains("Theta="), "Explanation must contain the theta diagnostics.");
    }

    /// <summary>
    /// Corrected in Sprint 14, RE-corrected in Sprint 15.22. The original assertion ("noisy series
    /// should be able to produce a negative theta") is mathematically impossible under the OU formula
    /// (EstimatedTheta = 1.0 - Clamp(normalizedInnovation/InnovationScale, -1, 1), always in [0,1]) -
    /// that Sprint 14 correction still stands.
    ///
    /// Sprint 15.22 (QDE-012 InnovationStd correction) changed the premise of the Sprint 14 fix's own
    /// exact-zero assertion. KalmanFilterModel now bounds the observations it uses to the most recent
    /// ObservationWindowSize=20 (was: the WHOLE supplied history) - see KalmanFilterModel.cs's doc
    /// comment and QDE-012_Sprint_15.21/15.22 reports. Sprint 14's "30 flat + 1 shock" construction
    /// relied on padding the WHOLE-history variance estimate with arbitrarily many flat observations to
    /// manufacture an arbitrarily large NormalizedInnovation (measured ~3.67, past the InnovationScale=
    /// 3.0 boundary) - exactly the unbounded-history pathology Sprint 15.22 fixes. Re-measured directly
    /// against the corrected model (Sprint 15.22 shock-magnitude/flat-count probe): NormalizedInnovation
    /// for a single terminal outlier is now bounded by the 20-observation window and asymptotes to
    /// ~2.97 as the window fills (19 flat + 1 shock) - it can no longer reach 3.0 via this construction,
    /// for any shock magnitude (confirmed scale-invariant, unchanged property) or any number of leading
    /// flat observations beyond the window size. This is not a defect: it is the direct, intended
    /// consequence of no longer letting arbitrary amounts of stale history manufacture an unbounded
    /// ratio. The test is corrected again to assert the real, achievable near-zero floor for a
    /// window-filling shock, instead of the no-longer-reachable exact zero, while preserving the
    /// original intent: a shock that fully dominates the observation window should drive mean-reversion
    /// strength to (near) zero.
    /// </summary>
    private static void AssertLargeShockDrivesThetaAndStrengthToZero()
    {
        // 19 flat observations + 1 final shock = 20 total, exactly filling KalmanFilterModel's
        // ObservationWindowSize (Sprint 15.22) - the shock-dominance scenario that comes closest to the
        // window's structural ceiling for NormalizedInnovation. NormalizedInnovation is scale-invariant
        // in the shock's magnitude (verified: 200 through 5000 all produce the identical ratio), so the
        // shock's absolute size is not the lever here - the flat-observation count relative to the
        // window is.
        var history = new List<decimal>();
        for (int i = 0; i < 19; i++)
        {
            history.Add(100m);
        }
        history.Add(1000m);

        var context = CreateContext(history, 1000m);
        var result = new OrnsteinUhlenbeckModel().Evaluate(context);

        Assert(result.Success, "A large final shock must still return a successful result.");
        if (result.Metrics is null)
            throw new InvalidOperationException("Metrics must be present.");

        if (!result.Metrics.TryGetValue("EstimatedTheta", out var thetaValueObj) || thetaValueObj is not double thetaValue)
            throw new InvalidOperationException("Metrics must contain EstimatedTheta as a double.");
        Assert(thetaValue >= 0.0 && thetaValue <= 1.0, "EstimatedTheta must always stay within its real, provable range of [0, 1].");
        Assert(
            thetaValue < 0.05,
            $"A shock filling the whole observation window must drive theta close to its practical floor for this construction (measured ~0.0096 empirically; 0.05 leaves headroom) - got {thetaValue:F4}.");
        if (!result.Metrics.TryGetValue("MeanReversionStrength", out var strengthValueObj) || strengthValueObj is not double strengthValue)
            throw new InvalidOperationException("Metrics must contain MeanReversionStrength as a double.");
        Assert(strengthValue < 0.05, $"Theta near its practical floor must produce near-zero mean reversion strength - got {strengthValue:F4}.");
    }

    private static void AssertRandomSeriesRemainsNumericallyStable()
    {
        var history = new decimal[] { 108m, 92m, 113m, 89m, 105m, 95m, 110m, 90m };
        var context = CreateContext(history, 107m);
        var result = new OrnsteinUhlenbeckModel().Evaluate(context);

        Assert(result.Success, "Random series must return a successful result.");
        Assert(!double.IsNaN(result.Score), "Score must not be NaN.");
        Assert(!double.IsInfinity(result.Score), "Score must not be Infinity.");
        Assert(result.Metrics is not null, "Metrics must be present.");

        foreach (var metric in result.Metrics!)
        {
            Assert(metric.Value is double, $"Metric '{metric.Key}' must be a double.");
            Assert(!double.IsNaN((double)metric.Value), $"Metric '{metric.Key}' must not be NaN.");
            Assert(!double.IsInfinity((double)metric.Value), $"Metric '{metric.Key}' must not be Infinity.");
        }
    }

    private static void AssertOutputMetricsAreKalmanCompatible()
    {
        var history = new decimal[] { 100m, 102m, 98m, 101m, 99m, 100m };
        var context = CreateContext(history, 100m);
        var result = new OrnsteinUhlenbeckModel().Evaluate(context);

        Assert(result.Success, "OU model must return a successful result when Kalman metrics are valid.");
        if (result.Metrics is null)
            throw new InvalidOperationException("Metrics must be present.");

        Assert(result.Metrics.TryGetValue("EstimatedMean", out var estimatedMeanValueObj) && estimatedMeanValueObj is double estimatedMeanValue, "Metrics must contain EstimatedMean as a double.");
        Assert(result.Metrics.TryGetValue("KalmanGain", out var kalmanGainValueObj) && kalmanGainValueObj is double kalmanGainValue, "Metrics must contain KalmanGain as a double.");
        Assert(result.Metrics.TryGetValue("HalfLife", out var halfLifeValueObj) && halfLifeValueObj is double halfLifeValue, "Metrics must contain HalfLife as a double.");
        Assert(result.Metrics.TryGetValue("EstimatedTheta", out var thetaValueObj) && thetaValueObj is double thetaValue, "Metrics must contain EstimatedTheta as a double.");
        Assert(result.Metrics.TryGetValue("MeanReversionStrength", out var strengthValueObj) && strengthValueObj is double strengthValue, "Metrics must contain MeanReversionStrength as a double.");
        Assert(result.Metrics.TryGetValue("ExpectedDeviation", out var expectedDeviationValueObj) && expectedDeviationValueObj is double expectedDeviationValue, "Metrics must contain ExpectedDeviation as a double.");
        Assert(result.Metrics.TryGetValue("OUScore", out var ouScoreValueObj) && ouScoreValueObj is double ouScoreValue, "Metrics must contain OUScore as a double.");
    }

    /// <summary>
    /// Builds a realistic upstream context by actually executing KalmanFilterModel against the same
    /// history first, exactly as SignalEngine does in production (ScientificResults is threaded
    /// incrementally, one real model result at a time - see Engine/Signal/SignalEngine.cs). The
    /// previous version of this harness omitted ScientificResults entirely, so OrnsteinUhlenbeckModel's
    /// upstream-Kalman-required guard rejected every call before reaching the theta/half-life math -
    /// every one of this file's assertions passed without ever exercising the OU computation itself.
    /// </summary>
    private static ScientificModelContext CreateContext(IReadOnlyList<decimal> history, decimal currentBar)
    {
        var decisionResult = new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.75 };
        var methodologySelection = new MethodologySelection(
            decisionResult,
            new QuantitativeMethodology(
                "MeanReversionMethodology",
                "Mean Reversion Methodology",
                "OrnsteinUhlenbeck",
                new[] { "Kalman Filter", "Dynamic Z-Score", "Volatility Models", "BOCPD" },
                "SPRT",
                new[] { "MeanReverting" },
                "1.0",
                Array.Empty<string>()),
            DateTime.UtcNow,
            "1.0",
            "Mean Reversion selected.");

        var marketContext = new Engine.ScientificModels.Abstractions.MarketContext(DateTime.UtcNow, currentBar, history);

        var kalmanContext = new ScientificModelContext(
            marketContext,
            decisionResult,
            methodologySelection,
            Array.Empty<ScientificModelResult>());
        ScientificModelResult kalmanResult = new KalmanFilterModel().Evaluate(kalmanContext);

        if (!kalmanResult.Success)
        {
            throw new InvalidOperationException(
                $"Test setup invariant violated: KalmanFilterModel must succeed on the fixture history so OrnsteinUhlenbeckModel has a real upstream result to consume. Kalman explanation: {kalmanResult.Explanation}");
        }

        return new ScientificModelContext(
            marketContext,
            decisionResult,
            methodologySelection,
            new[] { kalmanResult });
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
