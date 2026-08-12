using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.HalfLife;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.Registry;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.10. Computes BarMetrics for bar T using ONLY series[0..T] (inclusive) - series[T+1..] is
/// never read here. This is the harness's single look-ahead boundary on the "signal" side;
/// OutcomeSimulator is the only class allowed to read future bars, and it never feeds anything back
/// into a BarMetrics.
///
/// Runs the REAL production classes (KalmanFilterModel, OrnsteinUhlenbeckModel, DynamicZScoreModel,
/// VolatilityModel - via ScientificModelRegistry's own wiring/order - and HalfLifeEvidence) exactly as
/// SignalEngine/RegimeEngine do in production, so calibration measures the system that actually runs,
/// not a reimplementation of its formulas. No production class is modified or subclassed.
///
/// StopLossCalibrationPocTests.AssertLookAheadSafety_TruncatingFutureBarsProducesIdenticalMetrics
/// verifies this boundary is real, not just documented: computing a bar's metrics against the full
/// series and against a copy truncated right after that bar must produce byte-identical results.
/// </summary>
public static class BarMetricsComputer
{
    // Matches RegimeEngine's own HalfLifeWindowSize/HalfLifeMinimumSampleSize constants exactly -
    // Sprint 15.10 audit finding: HalfLifeEvidence.Compute reads Series[0..SampleSize), so Series must
    // already BE the trailing window (oldest-first within it), not the full history handed through
    // unsliced.
    private const int HalfLifeWindowSize = 30;
    private const int HalfLifeMinimumSampleSize = 20;

    private static readonly ScientificModelRegistry Registry = new();
    private static readonly MethodologyEngine Methodology = new();

    public static BarMetrics Compute(IReadOnlyList<decimal> series, int barIndex)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (barIndex < 0 || barIndex >= series.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(barIndex));
        }

        var history = new List<decimal>(barIndex + 1);
        for (int i = 0; i <= barIndex; i++)
        {
            history.Add(series[i]);
        }

        double price = (double)series[barIndex];

        // Winner is fixed to MeanReverting and Confidence/AmbiguityScore are fixed to their most
        // permissive values: the harness studies the SL candidates' behavior GIVEN a MeanReverting
        // regime call already made (matching EntryTrigger's own current scope, Sprint 15.9 §5/§11),
        // not the regime-arbitration step itself - that would be a different, unrelated experiment.
        var decisionResult = new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 1.0, AmbiguityScore = 0.0 };
        MethodologySelection methodologySelection = Methodology.Evaluate(decisionResult);

        var marketContext = new MarketContext(
            Timestamp: DateTime.UtcNow,
            CurrentBar: barIndex,
            History: history,
            CurrentPrice: series[barIndex]);

        var results = new List<ScientificModelResult>();
        foreach (var model in Registry.Resolve(methodologySelection))
        {
            var context = new ScientificModelContext(marketContext, decisionResult, methodologySelection, results.AsReadOnly());
            results.Add(model.Evaluate(context));
        }

        ScientificModelResult? kalman = results.FirstOrDefault(r => r.ModelName == "KalmanFilterModel" && r.Success);
        ScientificModelResult? ou = results.FirstOrDefault(r => r.ModelName == "OrnsteinUhlenbeckModel" && r.Success);
        ScientificModelResult? dynamicZ = results.FirstOrDefault(r => r.ModelName == "DynamicZScoreModel" && r.Success);
        ScientificModelResult? volatility = results.FirstOrDefault(r => r.ModelName == "VolatilityModel" && r.Success);

        double? estimatedEquilibrium = GetDouble(kalman?.Metrics, "EstimatedMean");
        double? innovationStd = GetDouble(kalman?.Metrics, "InnovationStd");
        double? dynamicZScore = GetDouble(dynamicZ?.Metrics, "DynamicZScore");
        double? currentVolatility = GetDouble(volatility?.Metrics, "CurrentVolatility");
        string? volatilityRegime = GetString(volatility?.Metrics, "VolatilityRegime");
        double? volatilityPercentile = GetDouble(volatility?.Metrics, "VolatilityPercentile");

        double? equilibriumDistance = estimatedEquilibrium is double eq ? Math.Abs(price - eq) : null;

        bool modelsValid = kalman is not null && ou is not null && dynamicZ is not null && volatility is not null;

        int sampleSize = Math.Min(history.Count, HalfLifeWindowSize);
        var halfLifeSeries = new List<decimal>(sampleSize);
        for (int i = history.Count - sampleSize; i < history.Count; i++)
        {
            halfLifeSeries.Add(history[i]);
        }

        var evidenceContext = new EvidenceContext
        {
            Series = halfLifeSeries,
            SampleSize = sampleSize,
            MinimumSampleSize = HalfLifeMinimumSampleSize,
            WindowSize = HalfLifeWindowSize,
            Timestamp = DateTime.UtcNow
        };
        HalfLifeResult halfLifeResult = new HalfLifeEvidence().Compute(evidenceContext);

        return new BarMetrics(
            barIndex,
            price,
            modelsValid,
            estimatedEquilibrium,
            innovationStd,
            dynamicZScore,
            currentVolatility,
            volatilityRegime,
            volatilityPercentile,
            equilibriumDistance,
            halfLifeResult.IsValid,
            halfLifeResult.IsValid ? halfLifeResult.HalfLife : null,
            halfLifeResult.IsValid ? halfLifeResult.RSquared : null);
    }

    private static double? GetDouble(IReadOnlyDictionary<string, object>? metrics, string key)
    {
        if (metrics is not null && metrics.TryGetValue(key, out object? raw) && raw is double typed && double.IsFinite(typed))
        {
            return typed;
        }

        return null;
    }

    private static string? GetString(IReadOnlyDictionary<string, object>? metrics, string key)
    {
        if (metrics is not null && metrics.TryGetValue(key, out object? raw) && raw is string typed)
        {
            return typed;
        }

        return null;
    }
}
