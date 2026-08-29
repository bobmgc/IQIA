using System;
using System.Collections.Generic;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.TradePlan;

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
        int closed = 0, notExecutable = 0, invalidEntry = 0, insufficientFutureData = 0, invalidExit = 0, invalidStopTarget = 0, buy = 0, sell = 0;

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
                case PositionStatus.InvalidStopTarget: invalidStopTarget++; break;
            }
        }

        return new BacktestExecutionResult(
            TotalCount: positions.Count,
            ClosedCount: closed,
            NotExecutableCount: notExecutable,
            InvalidEntryCount: invalidEntry,
            InsufficientFutureDataCount: insufficientFutureData,
            InvalidExitCount: invalidExit,
            InvalidStopTargetCount: invalidStopTarget,
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

        // Audit 2026-08-29: a plan the builder rejected on a policy gate (PLAN_REJECTED, e.g.
        // RiskRewardRatio below TradeRiskParameters.MinRiskReward) still carries a BUY/SELL Direction,
        // EntryPrice and SL/TP - without this guard the backtest would simulate it exactly like an
        // accepted plan, so the gate would only relabel the dashboard and never actually filter a trade.
        if (candidate.TradePlanStatus == TradePlanStatus.PLAN_REJECTED)
        {
            return new SimulatedPosition(
                positionId, PositionStatus.NotExecutable,
                "TradePlan.Status is PLAN_REJECTED (failed a policy gate, e.g. RiskRewardRatio below MinRiskReward) - not executed.",
                candidate.Direction, candidate.SignalTimestamp, candidate.EntryPrice, candidate.SignalBarIndex,
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

        // Sprint 15.25 (Lot 15.5, brief §7-13, Option D - see the Lot 15.5 report for the full decision
        // record): TradePlan.StopLoss/TakeProfit are computed by VolatilityStopLossModel/TradePlanBuilder
        // against referencePrice (candidate.EntryPrice, the SIGNAL bar's reference price) - never against
        // entryPrice (the REAL fill, one bar later, Lot 14.10). CurrentVolatility-derived levels are
        // fundamentally a DISTANCE concept (a dispersion measure around a price), not an absolute-price
        // concept - so the scientifically coherent reconciliation preserves the DISTANCE
        // (|referencePrice - StopLoss|, |referencePrice - TakeProfit|, both already strictly positive by
        // construction - VolatilityStopLossModel/TradePlanBuilder each already guarantee this against this
        // exact referencePrice) and re-anchors it onto entryPrice, the only price this method - the one
        // place the real fill becomes known - can causally use. TradePlan.StopLoss/TakeProfit themselves
        // are NEVER mutated (single source of truth, brief §14): this is a deterministic, formally
        // documented DERIVATION, computed fresh here, never a second silently-diverging definition.
        // Reached using only already-known values (referencePrice/candidate.StopLoss/.TakeProfit were
        // fixed at signal time; entryPrice is this same fill bar already used for the entry itself) - no
        // new data, no look-ahead.
        //
        // PRE-RECONCILIATION VALIDITY (brief §5, distinct from - and checked BEFORE - the reconciliation
        // itself): the distance |referencePrice - StopLoss| is taken via Math.Abs, so it is defined
        // regardless of which side of referencePrice the original TradePlan.StopLoss happened to sit on -
        // reconciliation alone can therefore never detect a TradePlan that was ALREADY inconsistent with
        // its OWN reference price (VolatilityStopLossModel/TradePlanBuilder guarantee this never happens in
        // practice, but Lot 15.4's defense-in-depth check existed precisely for the case where an upstream
        // change ever violates that guarantee). That original-consistency check is therefore performed
        // HERE, against referencePrice, BEFORE any distance is derived from it - silently "fixing" a
        // malformed TradePlan.StopLoss via Math.Abs, rather than rejecting it, would defeat the exact
        // safeguard this lot inherits from Lot 15.4 (brief §5: "un TradePlan incohérent ne doit jamais être
        // exécuté silencieusement").
        if (candidate.StopLoss is decimal originalStopLoss)
        {
            bool originalStopOnCorrectSide = candidate.Direction == DirectionCandidate.BUY_CANDIDATE
                ? originalStopLoss < referencePrice
                : originalStopLoss > referencePrice;
            if (!originalStopOnCorrectSide)
            {
                return new SimulatedPosition(
                    positionId, PositionStatus.InvalidStopTarget,
                    $"TradePlan.StopLoss ({originalStopLoss}) is on the wrong side of its own signal reference " +
                        $"price ({referencePrice}) for {candidate.Direction} - malformed before any fill-price " +
                        "reconciliation was even attempted.",
                    candidate.Direction, entryTimestamp, entryPrice, entryBarIndex,
                    null, null, null, null, null, null, null);
            }
        }

        if (candidate.TakeProfit is decimal originalTakeProfit)
        {
            bool originalTargetOnCorrectSide = candidate.Direction == DirectionCandidate.BUY_CANDIDATE
                ? originalTakeProfit > referencePrice
                : originalTakeProfit < referencePrice;
            if (!originalTargetOnCorrectSide)
            {
                return new SimulatedPosition(
                    positionId, PositionStatus.InvalidStopTarget,
                    $"TradePlan.TakeProfit ({originalTakeProfit}) is on the wrong side of its own signal reference " +
                        $"price ({referencePrice}) for {candidate.Direction} - malformed before any fill-price " +
                        "reconciliation was even attempted.",
                    candidate.Direction, entryTimestamp, entryPrice, entryBarIndex,
                    null, null, null, null, null, null, null);
            }
        }

        decimal? stopLossForExecution = candidate.StopLoss is decimal signalStopLoss
            ? candidate.Direction == DirectionCandidate.BUY_CANDIDATE
                ? entryPrice - Math.Abs(referencePrice - signalStopLoss)
                : entryPrice + Math.Abs(referencePrice - signalStopLoss)
            : null;

        decimal? takeProfitForExecution = candidate.TakeProfit is decimal signalTakeProfit
            ? candidate.Direction == DirectionCandidate.BUY_CANDIDATE
                ? entryPrice + Math.Abs(referencePrice - signalTakeProfit)
                : entryPrice - Math.Abs(referencePrice - signalTakeProfit)
            : null;

        // Sprint 15.25 (Lot 15.4, brief §5, RETAINED as defense in depth under Lot 15.5's reconciliation):
        // direction invariant - never execute an inconsistent level silently. By construction (see above),
        // stopLossForExecution/takeProfitForExecution are always strictly on the correct side of entryPrice
        // whenever the source TradePlan value was non-null - this check should therefore never fire via
        // this path any more (empirically confirmed, Lot 15.5 report §21), but is kept exactly as Lot 15.4
        // left it (same PositionStatus, same diagnostic shape) rather than removed, in case a future,
        // unrelated change upstream ever reintroduces a violation.
        if (stopLossForExecution is decimal candidateStopLoss)
        {
            bool stopOnCorrectSide = candidate.Direction == DirectionCandidate.BUY_CANDIDATE
                ? candidateStopLoss < entryPrice
                : candidateStopLoss > entryPrice;
            if (!stopOnCorrectSide)
            {
                return new SimulatedPosition(
                    positionId, PositionStatus.InvalidStopTarget,
                    $"Reconciled StopLoss ({candidateStopLoss}, from TradePlan.StopLoss={candidate.StopLoss} " +
                        $"anchored at signal reference price={referencePrice}) is on the wrong side of the real " +
                        $"EntryPrice ({entryPrice}) for {candidate.Direction}.",
                    candidate.Direction, entryTimestamp, entryPrice, entryBarIndex,
                    null, null, null, null, null, null, null);
            }
        }

        if (takeProfitForExecution is decimal candidateTakeProfit)
        {
            bool targetOnCorrectSide = candidate.Direction == DirectionCandidate.BUY_CANDIDATE
                ? candidateTakeProfit > entryPrice
                : candidateTakeProfit < entryPrice;
            if (!targetOnCorrectSide)
            {
                return new SimulatedPosition(
                    positionId, PositionStatus.InvalidStopTarget,
                    $"Reconciled TakeProfit ({candidateTakeProfit}, from TradePlan.TakeProfit={candidate.TakeProfit} " +
                        $"anchored at signal reference price={referencePrice}) is on the wrong side of the real " +
                        $"EntryPrice ({entryPrice}) for {candidate.Direction}.",
                    candidate.Direction, entryTimestamp, entryPrice, entryBarIndex,
                    null, null, null, null, null, null, null);
            }
        }

        int exitBarIndex = entryBarIndex + configuration.HorizonBars;

        // Brief §18: never truncate the horizon - a position too close to the end of the series is
        // INSUFFICIENT_FUTURE_DATA, never a CLOSED position with an invented exit. Sprint 15.25 (Lot
        // 15.4): deliberately UNCHANGED even though a Stop/Target might resolve the position long before
        // the horizon bar - requiring the FULL horizon window to exist upfront keeps data-coverage
        // requirements independent of the eventual outcome. Allowing an early Stop/Target hit to bypass
        // this check would let positions near the end of the series resolve to CLOSED only when they
        // happen to hit early and INSUFFICIENT_FUTURE_DATA otherwise - a subtle, outcome-correlated
        // coverage bias this lot must not introduce (see the Lot 15.4 report §14 for the full reasoning).
        if (exitBarIndex >= bars.Count)
        {
            return new SimulatedPosition(
                positionId, PositionStatus.InsufficientFutureData,
                $"TIME_HORIZON exit requires bar index {exitBarIndex}, but only {bars.Count} bars (0..{bars.Count - 1}) are available.",
                candidate.Direction, entryTimestamp, entryPrice, entryBarIndex,
                null, null, null, null, null, null, null);
        }

        // Sprint 15.25 (Lot 15.4, brief §6/§7/§9): intrabar Stop/Target monitoring, only when at least one
        // level was actually resolved by TradePlan - when neither exists, every line below this block is
        // skipped and behavior is BIT-IDENTICAL to before this lot (brief §31 backward compatibility).
        //
        // WINDOW: bars[entryBarIndex..exitBarIndex] INCLUSIVE - deliberately starting at entryBarIndex
        // itself (brief §9: "ne pas supposer" whether the entry bar can also trigger). Reasoned, not
        // assumed: EntryPrice is that bar's OWN Open, the first chronological event of an OHLC bar - the
        // position is therefore already open for the remainder of that same bar's High/Low/Close, so
        // checking them is a causal read of data concurrent with (never prior to) the fill, not
        // look-ahead. Ending at exitBarIndex INCLUSIVE means a Stop/Target touched exactly on the horizon
        // bar takes priority over a TIME_HORIZON exit at that same bar (brief §18) - the fallback below
        // only runs if this loop completes without a single trigger.
        if (stopLossForExecution is not null || takeProfitForExecution is not null)
        {
            for (int monitoredBarIndex = entryBarIndex; monitoredBarIndex <= exitBarIndex; monitoredBarIndex++)
            {
                HistoricalBar monitoredBar = bars[monitoredBarIndex];

                // Brief §21 extended to every intrabar-monitored bar, not just the final horizon bar.
                if (!monitoredBar.IsValid)
                {
                    return new SimulatedPosition(
                        positionId, PositionStatus.InvalidExit,
                        $"Intrabar-monitored bar[{monitoredBarIndex}] failed HistoricalBar.Validate(): {string.Join("; ", monitoredBar.Validate())}.",
                        candidate.Direction, entryTimestamp, entryPrice, entryBarIndex,
                        null, null, null, null, null, null, null);
                }

                // Sprint 15.25 (Lot 15.5): monitored against the RECONCILED levels (stopLossForExecution/
                // takeProfitForExecution), never the raw TradePlan.StopLoss/.TakeProfit directly - see the
                // reconciliation derivation above.
                bool stopTouched = stopLossForExecution is decimal stopLevel &&
                    (candidate.Direction == DirectionCandidate.BUY_CANDIDATE
                        ? monitoredBar.Low <= stopLevel
                        : monitoredBar.High >= stopLevel);
                bool targetTouched = takeProfitForExecution is decimal targetLevel &&
                    (candidate.Direction == DirectionCandidate.BUY_CANDIDATE
                        ? monitoredBar.High >= targetLevel
                        : monitoredBar.Low <= targetLevel);

                if (!stopTouched && !targetTouched)
                    continue;

                // Brief §7/§8: same-bar ambiguity when BOTH are touched - never invent an order. ExitPrice
                // uses the conservative (reconciled StopLoss) convention for Ambiguous, documented on
                // ExitReason.
                (decimal intrabarExitPrice, ExitReason intrabarExitReason) = stopTouched && targetTouched
                    ? (stopLossForExecution!.Value, ExitReason.Ambiguous)
                    : stopTouched
                        ? (stopLossForExecution!.Value, ExitReason.StopLoss)
                        : (takeProfitForExecution!.Value, ExitReason.TakeProfit);

                int intrabarHoldingBars = monitoredBarIndex - entryBarIndex;
                decimal intrabarGrossPriceMove = candidate.Direction == DirectionCandidate.BUY_CANDIDATE
                    ? intrabarExitPrice - entryPrice
                    : entryPrice - intrabarExitPrice;
                double intrabarEntryAsDouble = (double)entryPrice;
                double intrabarExitAsDouble = (double)intrabarExitPrice;
                double intrabarReturnValue = candidate.Direction == DirectionCandidate.BUY_CANDIDATE
                    ? (intrabarExitAsDouble - intrabarEntryAsDouble) / intrabarEntryAsDouble
                    : (intrabarEntryAsDouble - intrabarExitAsDouble) / intrabarEntryAsDouble;

                return new SimulatedPosition(
                    positionId, PositionStatus.Closed, null,
                    candidate.Direction, entryTimestamp, entryPrice, entryBarIndex,
                    monitoredBar.Timestamp, intrabarExitPrice, monitoredBarIndex, intrabarExitReason,
                    intrabarHoldingBars, intrabarGrossPriceMove, intrabarReturnValue);
            }
            // Fell through the whole window without a single Stop/Target touch - falls through to the
            // unchanged TIME_HORIZON logic below (exitBar was already proven valid, as the last iteration
            // of this loop covered monitoredBarIndex == exitBarIndex).
        }

        HistoricalBar exitBar = bars[exitBarIndex];

        // Brief §21: never compute a CLOSED position from invalid exit-bar data. Reached unconditionally
        // when no StopLoss/TakeProfit was configured at all (the only validation of exitBar in that case);
        // reached again, harmlessly, when one was configured but never triggered (exitBar was already
        // validated inside the loop above).
        if (!exitBar.IsValid)
        {
            return new SimulatedPosition(
                positionId, PositionStatus.InvalidExit,
                $"Exit bar[{exitBarIndex}] failed HistoricalBar.Validate(): {string.Join("; ", exitBar.Validate())}.",
                candidate.Direction, entryTimestamp, entryPrice, entryBarIndex,
                null, null, null, null, null, null, null);
        }

        // Brief §11/§12/§13: THEORETICAL_CLOSE_EXIT - ExitPrice is the exit bar's Close (never High/Low/
        // Open, never an assumption that a real order would have filled exactly there); ExitTimestamp is
        // that same bar's real Timestamp (never DateTime.Now/UtcNow). Sprint 15.25 (Lot 15.4): this
        // fallback is UNCHANGED from before this lot - reached only when Stop/Target were never configured,
        // or configured but never touched within the horizon window.
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
