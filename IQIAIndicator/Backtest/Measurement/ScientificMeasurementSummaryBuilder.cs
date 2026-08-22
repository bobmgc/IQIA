using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Backtest.Measurement;

/// <summary>
/// Sprint 15.25 (Lot 14.4, brief §32/§33/§34). Builds ALL/BUY/SELL <see cref="ScientificMeasurementSummary"/>
/// from a population of <see cref="MeasurementResult"/>. Stateless (static class, no fields) - same
/// isolation guarantee as <see cref="ScientificMeasurementEngine"/>.
/// </summary>
public static class ScientificMeasurementSummaryBuilder
{
    /// <summary>Assumes every <see cref="MeasurementResult"/> in <paramref name="measurements"/> that
    /// reached <see cref="MeasurementStatus.Measured"/> carries the SAME threshold set, in the same
    /// order (true whenever they all came from one <see cref="ScientificMeasurementEngine.MeasureAll"/>
    /// call with one <see cref="MeasurementConfiguration"/> - the normal, and only, way this lot produces
    /// them). Hit rates are matched by INDEX, not by threshold value equality, to avoid comparing
    /// doubles for equality.</summary>
    public static ScientificMeasurementSummaryByDirection Summarize(IReadOnlyList<MeasurementResult> measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);

        List<MeasurementResult> measured = measurements.Where(m => m.Status == MeasurementStatus.Measured).ToList();
        List<MeasurementResult> buy = measured.Where(m => m.Direction == DirectionCandidate.BUY_CANDIDATE).ToList();
        List<MeasurementResult> sell = measured.Where(m => m.Direction == DirectionCandidate.SELL_CANDIDATE).ToList();

        return new ScientificMeasurementSummaryByDirection(
            All: BuildSummary(measured),
            Buy: BuildSummary(buy),
            Sell: BuildSummary(sell));
    }

    private static ScientificMeasurementSummary BuildSummary(IReadOnlyList<MeasurementResult> measured)
    {
        double? medianReturn = Median(measured.Select(m => m.Return!.Value).ToList());
        double? medianMfe = Median(measured.Select(m => m.Mfe!.Value).ToList());
        double? medianMae = Median(measured.Select(m => m.Mae!.Value).ToList());

        var hitRates = new List<MeasurementHitRateResult>();
        if (measured.Count > 0)
        {
            int thresholdCount = measured[0].HitResults.Count;
            for (int t = 0; t < thresholdCount; t++)
            {
                double threshold = measured[0].HitResults[t].Threshold;
                int hitCount = measured.Count(m => m.HitResults[t].Hit);
                hitRates.Add(new MeasurementHitRateResult(threshold, (double)hitCount / measured.Count, hitCount, measured.Count));
            }
        }

        return new ScientificMeasurementSummary(measured.Count, medianReturn, medianMfe, medianMae, hitRates.AsReadOnly());
    }

    /// <summary>
    /// Sprint 15.25 (Lot 14.4, brief §33): exact, documented median convention - no opaque library call.
    /// Sorts ascending; an ODD count returns the single middle element; an EVEN count returns the
    /// arithmetic average of the two middle elements; an EMPTY input returns null (never 0.0 - a median
    /// of nothing is not zero, it is undefined).
    /// </summary>
    public static double? Median(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
            return null;

        double[] sorted = values.OrderBy(value => value).ToArray();
        int n = sorted.Length;

        return n % 2 == 1
            ? sorted[n / 2]
            : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }
}
