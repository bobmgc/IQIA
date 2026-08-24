using System;
using System.Collections.Generic;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Backtest.Execution;

/// <summary>
/// Sprint 15.25 (Lot 14.5, brief §2/§4). Turns an <see cref="ExecutionCandidate"/> into a deterministic
/// <see cref="SimulatedPosition"/> using the TIME_HORIZON exit convention - the only exit rule this lot
/// implements (brief §10/§45/§46). Answers "given a fixed entry/exit convention, what theoretical
/// position would have existed?" - a strictly different question from Lot 14.4's
/// <see cref="Backtest.Measurement.ScientificMeasurementEngine"/> ("what happened after the signal,
/// regardless of any exit convention?"), and the two engines never call into each other (brief §4/§35:
/// "Ne pas mélanger Measurement et Execution").
///
/// STATELESS BY DESIGN (brief §30): static class, no fields - nothing to isolate between calls.
///
/// NO PORTFOLIO ACCOUNTING (brief §31/§32): <see cref="SimulateAll"/> treats every signal as an
/// independent theoretical position. Overlapping positions (two signals whose TIME_HORIZON windows
/// intersect) are both simulated in full - no capital, no quantity, no margin constraint of any kind
/// suppresses either one. That policy belongs to a future lot.
///
/// NOT an order/broker/Risk Engine/ATAS integration (brief §3/§42/§47) - verified by grep: this file
/// references only <see cref="Core.MarketData"/> and <see cref="Engine.EntryTrigger"/> types.
/// </summary>
public static class ExecutionSimulator
{
    /// <summary>Friendly entry point (brief §4): extracts the <see cref="ExecutionCandidate"/> from
    /// <paramref name="signal"/> and delegates to <see cref="Simulate"/>.</summary>
    public static SimulatedPosition SimulateFromSignal(HistoricalSeries series, BacktestSignalResult signal, ExecutionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(signal);

        ExecutionCandidate candidate = ExecutionCandidate.FromSignal(signal);
        return Simulate(series, candidate, configuration);
    }

    public static SimulatedPosition Simulate(HistoricalSeries series, ExecutionCandidate candidate, ExecutionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(configuration);

        return SimulateCore(series.Bars, candidate, configuration);
    }

    /// <summary>Simulates every signal in <paramref name="signals"/>, one position per signal, in order -
    /// never skips a bar silently (mirrors <see cref="Backtest.Measurement.ScientificMeasurementEngine.MeasureAll"/>'s
    /// contract).</summary>
    public static BacktestExecutionResult SimulateAll(
        HistoricalSeries series, IReadOnlyList<BacktestSignalResult> signals, ExecutionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(configuration);

        var positions = new List<SimulatedPosition>(signals.Count);
        int closed = 0, notExecutable = 0, invalidEntry = 0, insufficientFutureData = 0, invalidExit = 0, buy = 0, sell = 0;

        foreach (BacktestSignalResult signal in signals)
        {
            SimulatedPosition position = SimulateFromSignal(series, signal, configuration);
            positions.Add(position);

            switch (position.Status)
            {
                case PositionStatus.Closed:
                    closed++;
                    if (position.Direction == DirectionCandidate.BUY_CANDIDATE) buy++;
                    else if (position.Direction == DirectionCandidate.SELL_CANDIDATE) sell++;
                    break;
                case PositionStatus.NotExecutable: notExecutable++; break;
                case PositionStatus.InvalidEntry: invalidEntry++; break;
                case PositionStatus.InsufficientFutureData: insufficientFutureData++; break;
                case PositionStatus.InvalidExit: invalidExit++; break;
            }
        }

        return new BacktestExecutionResult(
            TotalCount: positions.Count,
            ClosedCount: closed,
            NotExecutableCount: notExecutable,
            InvalidEntryCount: invalidEntry,
            InsufficientFutureDataCount: insufficientFutureData,
            InvalidExitCount: invalidExit,
            BuyCount: buy,
            SellCount: sell,
            DeterministicHash: ExecutionFingerprint.ComputeHash(positions),
            Positions: positions.AsReadOnly());
    }

    /// <summary>
    /// The actual simulation logic, deliberately independent of <see cref="HistoricalSeries"/> (takes a
    /// raw bar list) so it is directly unit-testable with a hand-built <see cref="HistoricalBar"/> list
    /// that <see cref="HistoricalSeries.Create"/> would itself reject (brief §21's invalid-exit-bar test -
    /// see <see cref="PositionStatus.InvalidExit"/>'s doc comment for why that status is otherwise
    /// unreachable through the validating constructors).
    ///
    /// FILL CONVENTION (Sprint 15.25, Lot 14.10, P0-1): the real, tradeable entry price is
    /// bars[SignalBarIndex + 1].Open - never the signal bar's own Close/TradePlan.EntryPrice. A signal
    /// computed from information available up to and including bar i cannot honestly be filled AT bar i's
    /// own close; the earliest a real order could realistically execute is the next bar's open. This
    /// corrects the bias identified in Lot 13's architecture audit (defect L-2: "Entrer à Close[i] alors
    /// que le signal est dérivé de Close[i]") and left uncorrected by the original Lot 14.5 implementation.
    /// <paramref name="candidate"/>.EntryPrice (TradePlan.EntryPrice, the signal bar's own reference price)
    /// is still read once, as a data-quality gate on the SIGNAL bar itself (defense in depth: if the
    /// pipeline could not even establish a reference price at the signal bar, the candidate is rejected
    /// before a fill is even attempted) - it is never used as the traded price.
    ///
    /// LOOK-AHEAD BOUNDARY (brief §9/§18 of Lot 14.5, preserved under the corrected convention): every
    /// field of a CLOSED position is resolved from bars[SignalBarIndex+1] (the fill bar) and
    /// bars[SignalBarIndex+1+HorizonBars] (the exit bar) only - never any bar beyond the exit bar. This is
    /// still a look-ahead-safe convention: the fill bar is the bar immediately AFTER the one the signal was
    /// computed from (a real order placed after seeing bar i's close can only ever fill on bar i+1 or
    /// later), not a bar the signal itself depended on.
    /// </summary>
    public static SimulatedPosition SimulateCore(IReadOnlyList<HistoricalBar> bars, ExecutionCandidate candidate, ExecutionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(configuration);

        int positionId = candidate.SignalBarIndex;

        // Brief §7/§19: NO_ACTION/WATCH never create a position - no fabricated EntryPrice, no empty
        // position pretending to be a trade.
        if (candidate.Direction is not (DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE))
        {
            return new SimulatedPosition(
                positionId, PositionStatus.NotExecutable,
                $"Direction={candidate.Direction} is not a directional trade candidate - only BUY_CANDIDATE/SELL_CANDIDATE are executed.",
                candidate.Direction, candidate.SignalTimestamp, null, candidate.SignalBarIndex,
                null, null, null, null, null, null, null);
        }

        // Data-quality gate on the SIGNAL bar itself (see the FILL CONVENTION doc comment above): a single
        // explicit reference price (TradePlan.EntryPrice, see ExecutionCandidate.FromSignal) must exist and
        // be strictly positive before a fill is even attempted - never a silent fallback, never a division
        // by zero.
        if (candidate.EntryPrice is not decimal referencePrice || referencePrice <= 0m)
        {
            return new SimulatedPosition(
                positionId, PositionStatus.InvalidEntry,
                "TradePlan.EntryPrice (signal-bar reference price) is missing or not strictly positive.",
                candidate.Direction, candidate.SignalTimestamp, candidate.EntryPrice, candidate.SignalBarIndex,
                null, null, null, null, null, null, null);
        }

        int signalBarIndex = candidate.SignalBarIndex;
        if (signalBarIndex < 0 || signalBarIndex >= bars.Count)
            throw new ArgumentOutOfRangeException(nameof(candidate), signalBarIndex, "SignalBarIndex must index an existing bar.");

        int fillBarIndex = signalBarIndex + 1;

        // P0-1: the case where bar i+1 does not exist is explicit insufficient-future-data - never
        // truncated to the signal bar's own price, never a fabricated fill.
        if (fillBarIndex >= bars.Count)
        {
            return new SimulatedPosition(
                positionId, PositionStatus.InsufficientFutureData,
                $"Realistic fill requires bar index {fillBarIndex} (Open of the bar after the signal), but only {bars.Count} bars (0..{bars.Count - 1}) are available.",
                candidate.Direction, candidate.SignalTimestamp, null, signalBarIndex,
                null, null, null, null, null, null, null);
        }

        HistoricalBar fillBar = bars[fillBarIndex];

        // Never compute a fill from an invalid bar - same discipline as the exit-bar check below.
        if (!fillBar.IsValid)
        {
            return new SimulatedPosition(
                positionId, PositionStatus.InvalidEntry,
                $"Fill bar[{fillBarIndex}] (Open of the bar after the signal) failed HistoricalBar.Validate(): {string.Join("; ", fillBar.Validate())}.",
                candidate.Direction, candidate.SignalTimestamp, null, signalBarIndex,
                null, null, null, null, null, null, null);
        }

        decimal entryPrice = fillBar.Open;
        if (entryPrice <= 0m)
        {
            return new SimulatedPosition(
                positionId, PositionStatus.InvalidEntry,
                $"Fill bar[{fillBarIndex}]'s Open ({entryPrice}) is not strictly positive.",
                candidate.Direction, candidate.SignalTimestamp, null, signalBarIndex,
                null, null, null, null, null, null, null);
        }

        DateTime entryTimestamp = fillBar.Timestamp;
        int entryBarIndex = fillBarIndex;

        int exitBarIndex = entryBarIndex + configuration.HorizonBars;

        // Brief §18: never truncate the horizon - a position too close to the end of the series is
        // INSUFFICIENT_FUTURE_DATA, never a CLOSED position with an invented exit.
        if (exitBarIndex >= bars.Count)
        {
            return new SimulatedPosition(
                positionId, PositionStatus.InsufficientFutureData,
                $"TIME_HORIZON exit requires bar index {exitBarIndex}, but only {bars.Count} bars (0..{bars.Count - 1}) are available.",
                candidate.Direction, entryTimestamp, entryPrice, entryBarIndex,
                null, null, null, null, null, null, null);
        }

        HistoricalBar exitBar = bars[exitBarIndex];

        // Brief §21: never compute a CLOSED position from invalid exit-bar data.
        if (!exitBar.IsValid)
        {
            return new SimulatedPosition(
                positionId, PositionStatus.InvalidExit,
                $"Exit bar[{exitBarIndex}] failed HistoricalBar.Validate(): {string.Join("; ", exitBar.Validate())}.",
                candidate.Direction, entryTimestamp, entryPrice, entryBarIndex,
                null, null, null, null, null, null, null);
        }

        // Brief §11/§12: THEORETICAL_CLOSE_EXIT - ExitPrice is the exit bar's Close (never High/Low/Open,
        // never an assumption that a real order would have filled exactly there); ExitTimestamp is that
        // same bar's real Timestamp (never DateTime.Now/UtcNow).
        decimal exitPrice = exitBar.Close;
        DateTime exitTimestamp = exitBar.Timestamp;
        int holdingBars = exitBarIndex - entryBarIndex;

        // Brief §14: signed, capital-independent, positive = favorable to the direction.
        decimal grossPriceMove = candidate.Direction == DirectionCandidate.BUY_CANDIDATE
            ? exitPrice - entryPrice
            : entryPrice - exitPrice;

        // Sprint 15.25 (Lot 14.10, P0-1): this Return is DELIBERATELY NO LONGER bit-identical to Lot
        // 14.4's MeasurementResult.Return - Measurement answers "what happened after the signal, regardless
        // of any exit convention" (still anchored on the signal bar's own Close, unchanged), while this
        // engine now answers "what would a realistically fillable position have returned" (anchored on
        // Open[SignalBarIndex+1]). The two engines were only numerically identical before this lot because
        // both happened to use the same (biased) Close[i] entry convention - see
        // ExecutionMeasurementReturnCoherenceTests for the documented, intentional divergence this lot
        // introduces.
        double entryAsDouble = (double)entryPrice;
        double exitAsDouble = (double)exitPrice;
        double returnValue = candidate.Direction == DirectionCandidate.BUY_CANDIDATE
            ? (exitAsDouble - entryAsDouble) / entryAsDouble
            : (entryAsDouble - exitAsDouble) / entryAsDouble;

        return new SimulatedPosition(
            positionId, PositionStatus.Closed, null,
            candidate.Direction, entryTimestamp, entryPrice, entryBarIndex,
            exitTimestamp, exitPrice, exitBarIndex, ExitReason.TimeHorizon,
            holdingBars, grossPriceMove, returnValue);
    }
}
