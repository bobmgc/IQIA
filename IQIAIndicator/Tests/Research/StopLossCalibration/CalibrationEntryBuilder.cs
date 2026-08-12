using System.Collections.Generic;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.10. Scans a full series and builds one CalibrationEntry per signal-eligible bar.
///
/// Eligibility (mirrors production's existing, unchanged rules - see EntryTriggerBuilder -
/// restated here rather than imported, to keep this harness's dependency on production code limited
/// to the scientific model classes themselves):
/// - ModelsValid: Kalman + OrnsteinUhlenbeck + DynamicZScore + Volatility all succeeded at this bar.
/// - DynamicZScore != 0: a zero z-score has no direction (Sprint 15.7.1's PRICE_AT_EQUILIBRIUM case) -
///   never treated as a signal, exactly like production.
/// HalfLife validity is NOT an eligibility gate - Candidate A doesn't need it at all, and Candidate D's
/// analysis applies its own optional filter on top of already-eligible entries rather than excluding
/// entries at collection time.
/// </summary>
public static class CalibrationEntryBuilder
{
    // Matches BarMetricsComputer's HalfLifeWindowSize: no entry is considered before every metric this
    // harness measures has had a chance to leave its own warmup state at least once.
    public const int WarmupBars = 30;

    public static IReadOnlyList<CalibrationEntry> BuildEntries(string seriesName, IReadOnlyList<decimal> series, int horizon)
    {
        var entries = new List<CalibrationEntry>();
        int lastPossibleEntry = series.Count - 2; // needs >= 1 future bar to measure any outcome at all

        for (int t = WarmupBars; t <= lastPossibleEntry; t++)
        {
            BarMetrics metrics = BarMetricsComputer.Compute(series, t);
            if (!metrics.ModelsValid || metrics.DynamicZScore is not double z || z == 0.0 || metrics.EstimatedEquilibrium is not double eq)
            {
                continue;
            }

            SignalDirection direction = z < 0 ? SignalDirection.Buy : SignalDirection.Sell;
            OutcomeMeasurement outcome = OutcomeSimulator.Simulate(series, t, direction, metrics.Price, eq, horizon);
            entries.Add(new CalibrationEntry(seriesName, t, direction, metrics, outcome));
        }

        return entries;
    }
}
