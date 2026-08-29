using System;
using System.Collections.Generic;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.10. THE ONLY class in this harness allowed to read series[entryBarIndex+1 ..]. Takes
/// entryPrice and estimatedEquilibriumAtEntry as already-frozen parameters computed by
/// BarMetricsComputer at the entry bar - never recomputes them, never looks at anything before the
/// entry bar, and writes nothing back into a BarMetrics. This one-directional data flow (past ->
/// signal, future -> outcome only) is the harness's entire look-ahead defense; see
/// StopLossCalibrationPocTests for the automated check that it actually holds.
/// </summary>
public static class OutcomeSimulator
{
    public static OutcomeMeasurement Simulate(
        IReadOnlyList<decimal> series,
        int entryBarIndex,
        SignalDirection direction,
        double entryPrice,
        double estimatedEquilibriumAtEntry,
        int horizon)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (entryBarIndex < 0 || entryBarIndex >= series.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(entryBarIndex));
        }

        int barsAvailable = Math.Max(0, Math.Min(horizon, series.Count - 1 - entryBarIndex));
        var adverse = new double[barsAvailable];
        var favorable = new double[barsAvailable];
        int? equilibriumBar = null;
        double runningAdverse = 0.0;
        double runningFavorable = 0.0;

        for (int h = 1; h <= barsAvailable; h++)
        {
            double price = (double)series[entryBarIndex + h];
            double move = direction == SignalDirection.Buy ? price - entryPrice : entryPrice - price;

            runningAdverse = Math.Max(runningAdverse, -move);
            runningFavorable = Math.Max(runningFavorable, move);
            adverse[h - 1] = runningAdverse;
            favorable[h - 1] = runningFavorable;

            if (equilibriumBar is null)
            {
                bool reached = direction == SignalDirection.Buy
                    ? price >= estimatedEquilibriumAtEntry
                    : price <= estimatedEquilibriumAtEntry;
                if (reached)
                {
                    equilibriumBar = h;
                }
            }
        }

        double mae = barsAvailable > 0 ? adverse[barsAvailable - 1] : 0.0;
        double mfe = barsAvailable > 0 ? favorable[barsAvailable - 1] : 0.0;

        return new OutcomeMeasurement(entryBarIndex, horizon, barsAvailable, adverse, favorable, equilibriumBar, mae, mfe);
    }
}
