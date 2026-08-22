using System;
using System.Collections.Generic;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.TradePlan;

namespace IQIAIndicator.Backtest.Measurement;

/// <summary>
/// Sprint 15.25 (Lot 14.4, brief §2/§3). Measures what happens AFTER a signal, using only future bars
/// the signal itself never saw. Strictly separated from signal production (Lot 14.3's
/// <see cref="BacktestEngine.RunSignalPipeline"/>): this class only READS a <see cref="BacktestSignalResult"/>
/// already produced elsewhere - it never re-runs Regime/Decision/Signal/EntryTrigger/TradePlan, and it
/// never writes back into the object it read (brief §2: "NE JAMAIS permettre à la mesure future de
/// modifier le signal historique").
///
/// STATELESS BY DESIGN (brief §30): every member is a static, pure function of its parameters - there is
/// no instance, so there is no field to isolate between calls or between runs. The determinism/run
/// isolation tests (Tests/Backtest/Measurement/) exist to prove this empirically, not just by inspection.
///
/// NOT an execution/position/P&amp;L/cost/Risk engine (brief §1/§35/§36/§37/§38) - RiskEngine,
/// AccountState, PortfolioState, TradingManager and any ATAS type are never referenced anywhere in this
/// file (verified by grep - see the Lot 14.4 report).
/// </summary>
public static class ScientificMeasurementEngine
{
    /// <summary>
    /// Friendly entry point (brief §4/§39): extracts Direction/EntryPrice from
    /// <paramref name="signal"/>.TradePlan (the ONLY entry-price source this lot uses - brief §5: never a
    /// silent Close[i] fallback) and delegates the actual math to <see cref="MeasureCore"/>.
    /// </summary>
    public static MeasurementResult Measure(HistoricalSeries series, BacktestSignalResult signal, MeasurementConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(configuration);

        TradePlan? plan = signal.TradePlan;
        if (plan is null)
        {
            return NotMeasurable(signal, configuration,
                "No TradePlan was produced for this bar (the signal pipeline never reached an EntryTriggerCandidate, or the bar was Rejected/Exception).");
        }

        DirectionCandidate direction = plan.Direction;
        if (direction is not (DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE))
        {
            return NotMeasurable(signal, configuration,
                $"Direction={direction} is not a directional trade candidate - only BUY_CANDIDATE/SELL_CANDIDATE are measured (brief §6/§27).");
        }

        if (plan.EntryPrice is not decimal entryPrice || entryPrice <= 0m)
        {
            return new MeasurementResult(
                signal.BarIndex, signal.Timestamp, direction, plan.EntryPrice,
                MeasurementStatus.InvalidEntryPrice,
                "TradePlan.EntryPrice is missing or not strictly positive.",
                configuration.HorizonBars, null, null, null, Array.Empty<MeasurementHitResult>());
        }

        return MeasureCore(series.Bars, signal.BarIndex, signal.Timestamp, direction, entryPrice, configuration);
    }

    /// <summary>
    /// Measures every bar in <paramref name="signals"/> against <paramref name="series"/> (brief §39's
    /// minimal BacktestEngine integration point). Order-preserving, one <see cref="MeasurementResult"/>
    /// per input signal - never skips a bar silently.
    /// </summary>
    public static IReadOnlyList<MeasurementResult> MeasureAll(
        HistoricalSeries series, IReadOnlyList<BacktestSignalResult> signals, MeasurementConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(configuration);

        var results = new List<MeasurementResult>(signals.Count);
        foreach (BacktestSignalResult signal in signals)
            results.Add(Measure(series, signal, configuration));

        return results.AsReadOnly();
    }

    /// <summary>
    /// The actual measurement math, deliberately independent of <see cref="HistoricalSeries"/> (takes a
    /// raw bar list) so it is directly unit-testable with a hand-built <see cref="HistoricalBar"/> list
    /// that <see cref="HistoricalSeries.Create"/> would itself reject (brief §25's invalid-future-bar
    /// test - see <see cref="MeasurementStatus.InvalidFutureData"/>'s doc comment for why that status is
    /// otherwise unreachable through the validating constructors).
    ///
    /// LOOK-AHEAD BOUNDARY (brief §8): reads <paramref name="bars"/>[signalBarIndex] only to know where
    /// the horizon starts - never its own High/Low/Close. Reads
    /// <paramref name="bars"/>[signalBarIndex+1 .. signalBarIndex+HorizonBars] and NOTHING beyond that
    /// upper bound, and NOTHING at or before signalBarIndex.
    /// </summary>
    public static MeasurementResult MeasureCore(
        IReadOnlyList<HistoricalBar> bars,
        int signalBarIndex,
        DateTime signalTimestamp,
        DirectionCandidate direction,
        decimal entryPrice,
        MeasurementConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(configuration);

        if (signalBarIndex < 0 || signalBarIndex >= bars.Count)
            throw new ArgumentOutOfRangeException(nameof(signalBarIndex), signalBarIndex, "signalBarIndex must index an existing bar.");

        if (direction is not (DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE))
            throw new ArgumentException("direction must be BUY_CANDIDATE or SELL_CANDIDATE - callers must filter WATCH/NO_ACTION before calling MeasureCore.", nameof(direction));

        if (entryPrice <= 0m)
            throw new ArgumentException("entryPrice must be strictly positive - callers must filter invalid entry prices before calling MeasureCore.", nameof(entryPrice));

        int horizonEndIndex = signalBarIndex + configuration.HorizonBars;

        // Brief §9/§24: the full horizon must be available, or nothing is computed at all - never a
        // partial number reported as if it were complete. Documented choice (brief §50): a "PARTIAL"
        // status that still reports numbers computed from fewer bars than requested was considered and
        // rejected, precisely because it would be indistinguishable, downstream, from a genuinely
        // complete measurement unless every consumer remembered to check bar counts - see the Lot 14.4
        // report's Horizon/Partial section for the full rationale.
        if (horizonEndIndex >= bars.Count)
        {
            return new MeasurementResult(
                signalBarIndex, signalTimestamp, direction, entryPrice,
                MeasurementStatus.InsufficientFutureData,
                $"Horizon requires bar index {horizonEndIndex}, but only {bars.Count} bars (0..{bars.Count - 1}) are available.",
                configuration.HorizonBars, null, null, null, Array.Empty<MeasurementHitResult>());
        }

        // Brief §25: validate every future bar BEFORE computing anything - never a silent calculation
        // over invalid data, never a partially-contaminated MFE/MAE.
        for (int j = signalBarIndex + 1; j <= horizonEndIndex; j++)
        {
            HistoricalBar futureBar = bars[j];
            if (!futureBar.IsValid)
            {
                return new MeasurementResult(
                    signalBarIndex, signalTimestamp, direction, entryPrice,
                    MeasurementStatus.InvalidFutureData,
                    $"Bar[{j}] failed HistoricalBar.Validate(): {string.Join("; ", futureBar.Validate())}.",
                    configuration.HorizonBars, null, null, null, Array.Empty<MeasurementHitResult>());
            }
        }

        double entry = (double)entryPrice;
        double mfe = double.NegativeInfinity;
        double mae = double.NegativeInfinity;

        // Brief §10/§11: MFE/MAE are the MAXIMUM of the per-bar favorable/adverse excursion over
        // j = signalBarIndex+1 .. horizonEndIndex - literally as specified, never clamped to >= 0. If
        // price only ever moves in one direction throughout the horizon, the "maximum" on the other side
        // is itself negative - documented explicitly in the Lot 14.4 report rather than silently floored,
        // since the brief specifies "maximum", not "maximum(0, ...)".
        for (int j = signalBarIndex + 1; j <= horizonEndIndex; j++)
        {
            HistoricalBar bar = bars[j];
            double high = (double)bar.High;
            double low = (double)bar.Low;

            double favorable = direction == DirectionCandidate.BUY_CANDIDATE
                ? (high - entry) / entry
                : (entry - low) / entry;
            double adverse = direction == DirectionCandidate.BUY_CANDIDATE
                ? (entry - low) / entry
                : (high - entry) / entry;

            if (favorable > mfe) mfe = favorable;
            if (adverse > mae) mae = adverse;
        }

        // Brief §12: FINAL Return uses Close at the horizon's LAST bar (i+HorizonBars), not an
        // intermediate bar - distinct from the per-bar favorable/adverse terms above, which use High/Low.
        double finalClose = (double)bars[horizonEndIndex].Close;
        double finalReturn = direction == DirectionCandidate.BUY_CANDIDATE
            ? (finalClose - entry) / entry
            : (entry - finalClose) / entry;

        // Brief §13/§22: Hit(threshold) = "did price reach the threshold favorably at any point in the
        // horizon" = "does the MAXIMUM favorable excursion reach the threshold" - MFE already IS that
        // maximum (same per-bar formula), so Hit is read directly off it rather than re-scanning
        // High/Low a second time. Mathematically identical to the brief's literal
        // "High[j] >= Entry*(1+threshold) for some j" / "Low[j] <= Entry*(1-threshold) for some j"
        // formulation - see the Lot 14.4 report for the equivalence proof - and independently verified by
        // BuyMfeMaeTests/SellMfeMaeTests/HitRateBoundaryTests against hand-computed fixtures.
        var hitResults = new List<MeasurementHitResult>(configuration.HitThresholds.Count);
        foreach (double threshold in configuration.HitThresholds)
            hitResults.Add(new MeasurementHitResult(threshold, mfe >= threshold));

        return new MeasurementResult(
            signalBarIndex, signalTimestamp, direction, entryPrice,
            MeasurementStatus.Measured, null, configuration.HorizonBars,
            finalReturn, mfe, mae, hitResults.AsReadOnly());
    }

    private static MeasurementResult NotMeasurable(BacktestSignalResult signal, MeasurementConfiguration configuration, string reason) =>
        new(
            signal.BarIndex, signal.Timestamp,
            signal.TradePlan?.Direction ?? DirectionCandidate.NO_ACTION,
            signal.TradePlan?.EntryPrice,
            MeasurementStatus.NotMeasurable, reason, configuration.HorizonBars,
            null, null, null, Array.Empty<MeasurementHitResult>());
}
