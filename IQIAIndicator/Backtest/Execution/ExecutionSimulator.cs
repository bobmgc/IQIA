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
    /// LOOK-AHEAD BOUNDARY (brief §9/§18): EntryPrice/EntryTimestamp come exclusively from
    /// <paramref name="candidate"/> (already resolved at the signal bar by Lot 14.3's pipeline, using
    /// bars[0..EntryBarIndex] only - never recomputed here). The only bar this method itself reads from
    /// <paramref name="bars"/> is bars[EntryBarIndex + HorizonBars] (the exit bar) - never
    /// bars[EntryBarIndex] and never anything beyond the exit bar.
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
                candidate.Direction, candidate.SignalTimestamp, candidate.EntryPrice, candidate.SignalBarIndex,
                null, null, null, null, null, null, null);
        }

        // Brief §8/§20: a single explicit entry convention (TradePlan.EntryPrice, see
        // ExecutionCandidate.FromSignal) - never a silent fallback to Close/Open/High/Low, never a
        // division by zero.
        if (candidate.EntryPrice is not decimal entryPrice || entryPrice <= 0m)
        {
            return new SimulatedPosition(
                positionId, PositionStatus.InvalidEntry,
                "EntryPrice is missing or not strictly positive.",
                candidate.Direction, candidate.SignalTimestamp, candidate.EntryPrice, candidate.SignalBarIndex,
                null, null, null, null, null, null, null);
        }

        int entryBarIndex = candidate.SignalBarIndex;
        if (entryBarIndex < 0 || entryBarIndex >= bars.Count)
            throw new ArgumentOutOfRangeException(nameof(candidate), entryBarIndex, "SignalBarIndex must index an existing bar.");

        int exitBarIndex = entryBarIndex + configuration.HorizonBars;

        // Brief §18: never truncate the horizon - a position too close to the end of the series is
        // INSUFFICIENT_FUTURE_DATA, never a CLOSED position with an invented exit.
        if (exitBarIndex >= bars.Count)
        {
            return new SimulatedPosition(
                positionId, PositionStatus.InsufficientFutureData,
                $"TIME_HORIZON exit requires bar index {exitBarIndex}, but only {bars.Count} bars (0..{bars.Count - 1}) are available.",
                candidate.Direction, candidate.SignalTimestamp, entryPrice, entryBarIndex,
                null, null, null, null, null, null, null);
        }

        HistoricalBar exitBar = bars[exitBarIndex];

        // Brief §21: never compute a CLOSED position from invalid exit-bar data.
        if (!exitBar.IsValid)
        {
            return new SimulatedPosition(
                positionId, PositionStatus.InvalidExit,
                $"Exit bar[{exitBarIndex}] failed HistoricalBar.Validate(): {string.Join("; ", exitBar.Validate())}.",
                candidate.Direction, candidate.SignalTimestamp, entryPrice, entryBarIndex,
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

        // Brief §15: identical convention to Lot 14.4's Return (Close at horizon end, same sign rule) -
        // not a second, competing definition. Verified independently by
        // ExecutionMeasurementReturnCoherenceTests, which runs both engines on the same data and asserts
        // bit-exact agreement.
        double entryAsDouble = (double)entryPrice;
        double exitAsDouble = (double)exitPrice;
        double returnValue = candidate.Direction == DirectionCandidate.BUY_CANDIDATE
            ? (exitAsDouble - entryAsDouble) / entryAsDouble
            : (entryAsDouble - exitAsDouble) / entryAsDouble;

        return new SimulatedPosition(
            positionId, PositionStatus.Closed, null,
            candidate.Direction, candidate.SignalTimestamp, entryPrice, entryBarIndex,
            exitTimestamp, exitPrice, exitBarIndex, ExitReason.TimeHorizon,
            holdingBars, grossPriceMove, returnValue);
    }
}
