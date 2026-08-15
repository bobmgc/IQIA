using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificModels.MeanReversion;

/// <summary>
/// Sprint 15.22 (QDE-012 InnovationStd correction): before this sprint, <see cref="Evaluate"/> fed
/// EVERY observation in <c>context.MarketContext.History</c> (cumulative since the start of the
/// series) into <see cref="EstimateMeasurementNoise"/>/<see cref="EstimateInitialVariance"/> and the
/// Kalman recursion. On a stationary process (bounded around a fixed level - every synthetic
/// QDE-012 golden dataset except RandomWalk) the resulting <c>InnovationStd</c> stays roughly flat
/// regardless of history length, but on a NON-stationary process (a real ES price level, or a
/// synthetic RandomWalk) it grows with the amount of history supplied - proven on the real Sprint
/// 15.19 ES/M5 capture (3.79 -&gt; 40.65 as history grew from 61 to 4279 bars, Spearman(index,
/// InnovationStd)=0.971 - QDE-012_Sprint_15.21 report) and reproduced byte-for-byte in character on a
/// synthetic RandomWalk of the same length (5.02 -&gt; 42.84), while WhiteNoise/MeanRevertingOu/
/// AR1(0.95) stayed flat. <see cref="ObservationWindowSize"/> bounds the observation window this
/// class uses internally to the most recent observations only, restoring independence from total
/// session length - see QDE-012_Sprint_15.21/15.22 reports for the full evidence trail. Nothing
/// outside this class changes: <c>context.MarketContext.History</c> itself, and every other consumer
/// of it, are untouched.
/// </summary>
public sealed class KalmanFilterModel : IScientificModel
{
    public string Name => "KalmanFilterModel";

    public string Category => "MeanReversion";

    private const double MinimumVariance = 1e-6;
    private const double MinimumNoise = 1e-6;
    private const double InnovationScale = 3.0;

    /// <summary>Sprint 15.22: bounds how many of the most recent observations feed noise estimation
    /// and the Kalman recursion - see class doc comment. Fewer observations than this available (but
    /// still &gt;=2, the pre-existing minimum) means all of them are used, unchanged from pre-15.22
    /// behavior for short histories (QDE-012_Sprint_15.22 report, documented warmup decision - Test 4).
    /// Selected by an 8-candidate empirical sweep (N=10/15/20/25/30/40/60/80) on the real Sprint 15.19
    /// ES/M5 capture plus synthetic RandomWalk at 3 protocol seeds (42/43/44) and 3 stationary families
    /// (WhiteNoise/MeanRevertingOu/AR1(0.95)) - see QDE-012_Sprint_15.22 report §5-11 for the full
    /// multi-criteria table. N=20 and N=25 were the two strongest, closely-matched candidates (N=25
    /// marginally ahead on regime/HalfLife correlation and AR1(0.95) sub-period stability; N=20
    /// marginally ahead on market/Range correlation); N=20 is kept as the tie-break because it already
    /// matches an existing, independent convention elsewhere in this same codebase's local-window
    /// models (VolatilityModel.CurrentVolatilityWindow=20; HalfLifeEvidence/VarianceRatioEvidence/
    /// CusumEvidence's MinimumSampleSize=20) - not derived from QDE-012's Horizon=40 by coincidence of
    /// naming, and not chosen from correlation-with-Range alone (Sprint 15.22 report §14 rule).</summary>
    private const int ObservationWindowSize = 20;

    public ScientificModelResult Evaluate(ScientificModelContext context)
    {
        bool compatible =
            context.DecisionResult.Winner == MarketState.MeanReverting &&
            context.MethodologySelection.SelectedMethodology.Name == "MeanReversionMethodology";

        if (!compatible)
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "The selected methodology and decision winner are not compatible with the Mean Reversion scientific stack.");
        }

        IReadOnlyList<decimal> history = context.MarketContext.History;
        if (history is null || history.Count < 2)
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "KalmanFilterModel requires at least two historical observations to estimate an equilibrium.");
        }

        // Sprint 15.22: bound the observation window to the most recent ObservationWindowSize
        // observations (or all of them if fewer are available - "warmup" behavior, unchanged from
        // pre-15.22 for short histories). MarketContext.History itself is never touched - this slice
        // is local to this method only. See class doc comment for why.
        int windowStart = Math.Max(0, history.Count - ObservationWindowSize);
        IReadOnlyList<decimal> windowedHistory = windowStart == 0 ? history : history.Skip(windowStart).ToList();

        // Use the last element of the windowed history as the current observation (always the same
        // value as history[history.Count-1] - the window always ends at the most recent bar).
        double currentPrice = (double)windowedHistory[windowedHistory.Count - 1];
        double[] observations = windowedHistory.Select(x => (double)x).ToArray();
        if (observations.Any(double.IsNaN) || observations.Any(double.IsInfinity) || double.IsNaN(currentPrice) || double.IsInfinity(currentPrice))
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "KalmanFilterModel received invalid numeric values in the market context.");
        }

        double measurementNoise = EstimateMeasurementNoise(observations);
        double processNoise = Math.Max(measurementNoise * 0.1, MinimumNoise);

        double stateMean = observations[0];
        double stateCovariance = Math.Max(EstimateInitialVariance(observations), MinimumVariance);
        double lastInnovation = 0.0;
        double lastInnovationCovariance = stateCovariance + measurementNoise;
        double lastKalmanGain = 0.0;

        for (int i = 1; i < observations.Length; i++)
        {
            double predictedMean = stateMean;
            double predictedCovariance = stateCovariance + processNoise;

            double observation = observations[i];
            double innovation = observation - predictedMean;
            double innovationCovariance = predictedCovariance + measurementNoise;
            double kalmanGain = innovationCovariance <= 0.0
                ? 0.0
                : predictedCovariance / innovationCovariance;

            kalmanGain = Math.Clamp(kalmanGain, 0.0, 1.0);
            stateMean = predictedMean + kalmanGain * innovation;
            stateCovariance = Math.Max((1.0 - kalmanGain) * predictedCovariance, MinimumVariance);

            lastInnovation = innovation;
            lastInnovationCovariance = Math.Max(innovationCovariance, MinimumVariance);
            lastKalmanGain = kalmanGain;
        }

        double innovationAtCurrent = currentPrice - stateMean;
        double innovationCovarianceAtCurrent = stateCovariance + measurementNoise;
        double innovationStd = Math.Sqrt(Math.Max(innovationCovarianceAtCurrent, MinimumVariance));
        double normalizedInnovation = innovationStd <= 0.0
            ? 0.0
            : Math.Abs(innovationAtCurrent) / innovationStd;

        double score = 1.0 - Math.Clamp(normalizedInnovation / InnovationScale, 0.0, 1.0);

        string explanation =
            $"Estimated Mean={stateMean.ToString("F6", CultureInfo.InvariantCulture)}; " +
            $"Innovation={innovationAtCurrent.ToString("F6", CultureInfo.InvariantCulture)}; " +
            $"InnovationVariance={innovationCovarianceAtCurrent.ToString("F6", CultureInfo.InvariantCulture)}; " +
            $"KalmanGain={lastKalmanGain.ToString("F6", CultureInfo.InvariantCulture)}; " +
            $"FilterCovariance={stateCovariance.ToString("F6", CultureInfo.InvariantCulture)}.";

        var metrics = new Dictionary<string, object>
        {
            ["EstimatedMean"] = stateMean,
            ["CurrentPrice"] = currentPrice,
            ["Innovation"] = innovationAtCurrent,
            ["InnovationVariance"] = innovationCovarianceAtCurrent,
            ["InnovationStd"] = innovationStd,
            ["NormalizedInnovation"] = normalizedInnovation,
            ["KalmanGain"] = lastKalmanGain,
            ["FilterCovariance"] = stateCovariance,
            ["MeasurementNoise"] = measurementNoise,
            ["ProcessNoise"] = processNoise,
            // Sprint 15.22: diagnostic-only, additive metric - how many observations actually fed
            // this estimate (<= ObservationWindowSize; equals history.Count during warmup). Exists so
            // the window boundary is independently observable/testable, not just implied - see
            // KalmanFilterModelWindowTests.cs Test 3.
            ["ObservationsUsed"] = (double)observations.Length
        };

        return new ScientificModelResult(
            Name,
            true,
            score,
            explanation,
            metrics);
    }

    private static double EstimateMeasurementNoise(double[] observations)
    {
        if (observations.Length < 2)
            return 1.0;

        double mean = observations.Average();
        double variance = 0.0;
        foreach (double value in observations)
        {
            double delta = value - mean;
            variance += delta * delta;
        }

        variance /= observations.Length;
        return Math.Max(variance, MinimumNoise);
    }

    private static double EstimateInitialVariance(double[] observations)
    {
        if (observations.Length < 2)
            return 1.0;

        double sum = 0.0;
        double mean = observations.Average();
        foreach (double value in observations)
        {
            double delta = value - mean;
            sum += delta * delta;
        }

        return Math.Max(sum / (observations.Length - 1), MinimumVariance);
    }
}
