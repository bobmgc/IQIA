using System;
using System.Collections.Generic;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.EntryTrigger;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Execution;

/// <summary>
/// Sprint 15.25 (Lot 14.5, brief §22-§28, §39; rewritten in Lot 14.10, P0-1, for the corrected
/// Open[SignalBarIndex+1] fill convention). Pure formula-level coverage of
/// <see cref="ExecutionSimulator.SimulateCore"/> against hand-computed fixtures. Every fixture below now
/// carries one extra bar compared to the pre-Lot-14.10 version of this file: bar 0 is the signal bar
/// (never read for its price), bar 1 is the FILL bar (its Open is the real entry price), and the exit bar
/// sits HorizonBars after the fill bar - never after the signal bar.
/// </summary>
public sealed class ExecutionSimulatorFormulaTests
{
    private static readonly DateTime Anchor = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    private static HistoricalBar Bar(int minutesFromAnchor, decimal high, decimal low, decimal close, decimal? open = null) =>
        new(Anchor.AddMinutes(minutesFromAnchor), open ?? close, high, low, close, Volume: 100m);

    /// <summary>A flat, always-valid fill bar whose Open is <paramref name="fillOpen"/> - deliberately
    /// High=Low=Close=Open unless the caller needs otherwise, so every formula test's expected
    /// GrossPriceMove/Return depends only on this Open and the exit bar's Close.</summary>
    private static HistoricalBar FillBar(int minutesFromAnchor, decimal fillOpen) =>
        new(Anchor.AddMinutes(minutesFromAnchor), fillOpen, fillOpen, fillOpen, fillOpen, Volume: 100m);

    private static ExecutionCandidate Candidate(int barIndex, DirectionCandidate direction, decimal? entryPrice) =>
        new(barIndex, Anchor.AddMinutes(barIndex * 5), direction, entryPrice, null);

    // ── §22: BUY fixture (signal bar 0, fill bar 1 @ Open=100, exit bar 2 @ Close=105) ────────────────

    [Fact]
    public void Buy_Wins_GrossPriceMoveAndReturnMatchHandComputedValues()
    {
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m), FillBar(5, 100m), Bar(10, 106m, 104m, 105m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(5m, position.GrossPriceMove);
        Assert.Equal(0.05, position.Return!.Value, 9);
    }

    // ── §23: SELL fixture ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sell_Wins_GrossPriceMoveAndReturnMatchHandComputedValues()
    {
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m), FillBar(5, 100m), Bar(10, 96m, 94m, 95m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(5m, position.GrossPriceMove);
        Assert.Equal(0.05, position.Return!.Value, 9);
    }

    // ── §24: loss fixtures ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Buy_Loses_GrossPriceMoveAndReturnAreNegative()
    {
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m), FillBar(5, 100m), Bar(10, 96m, 94m, 95m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m), ExecutionConfiguration.Create(1));

        Assert.Equal(-5m, position.GrossPriceMove);
        Assert.Equal(-0.05, position.Return!.Value, 9);
    }

    [Fact]
    public void Sell_Loses_GrossPriceMoveAndReturnAreNegative()
    {
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m), FillBar(5, 100m), Bar(10, 106m, 104m, 105m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m), ExecutionConfiguration.Create(1));

        Assert.Equal(-5m, position.GrossPriceMove);
        Assert.Equal(-0.05, position.Return!.Value, 9);
    }

    // ── §25: break-even fixtures ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DirectionCandidateKind.Buy)]
    [InlineData(DirectionCandidateKind.Sell)]
    public void BreakEven_ReturnAndGrossPriceMoveAreZero(DirectionCandidateKind kind)
    {
        DirectionCandidate direction = kind == DirectionCandidateKind.Buy ? DirectionCandidate.BUY_CANDIDATE : DirectionCandidate.SELL_CANDIDATE;
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m), FillBar(5, 100m), Bar(10, 100m, 100m, 100m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, direction, 100m), ExecutionConfiguration.Create(1));

        Assert.Equal(0m, position.GrossPriceMove);
        Assert.Equal(0.0, position.Return!.Value, 9);
    }

    public enum DirectionCandidateKind { Buy, Sell }

    // ── §13: holding bars (entry is now the FILL bar, index 1 - never the signal bar, index 0) ────────

    [Fact]
    public void HoldingBars_OneBarHorizon_IsExactlyOne()
    {
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m), FillBar(5, 100m), Bar(10, 101m, 99m, 100m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m), ExecutionConfiguration.Create(1));

        Assert.Equal(1, position.HoldingBars);
        Assert.Equal(1, position.EntryBarIndex);
        Assert.Equal(2, position.ExitBarIndex);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    public void HoldingBars_AlwaysEqualsConfiguredHorizon(int horizon)
    {
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m), FillBar(5, 100m) };
        for (int j = 1; j <= horizon; j++)
            bars.Add(Bar((j + 1) * 5, 101m, 99m, 100m));

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m), ExecutionConfiguration.Create(horizon));

        Assert.Equal(horizon, position.HoldingBars);
    }

    // ── §11/§12: THEORETICAL_CLOSE_EXIT convention (unchanged: exit is always the exit bar's Close) ───

    [Fact]
    public void ExitPrice_IsExitBarClose_NeverHighLowOrOpen()
    {
        var bars = new List<HistoricalBar>
        {
            Bar(0, 100m, 100m, 100m),
            FillBar(5, 100m),
            Bar(10, high: 110m, low: 90m, close: 103m, open: 95m)
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m), ExecutionConfiguration.Create(1));

        Assert.Equal(103m, position.ExitPrice);
        Assert.Equal(bars[2].Timestamp, position.ExitTimestamp);
        Assert.Equal(ExitReason.TimeHorizon, position.ExitReason);
    }

    // ── Entry uses the FILL bar's Open, never its Close (the whole point of Lot 14.10, P0-1) ──────────

    [Fact]
    public void Entry_UsesFillBarOpen_NeverFillBarClose_NeverSignalBarAnything()
    {
        var bars = new List<HistoricalBar>
        {
            Bar(0, high: 999m, low: 999m, close: 999m, open: 999m), // signal bar - must never be read for price
            new HistoricalBar(Anchor.AddMinutes(5), Open: 100m, High: 103m, Low: 99m, Close: 102m, Volume: 100m), // fill bar: Open != Close
            Bar(10, 105m, 105m, 105m) // exit bar
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 1m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(100m, position.EntryPrice); // the fill bar's Open, not its Close (102) nor the signal bar's price (999)
        Assert.Equal(bars[1].Timestamp, position.EntryTimestamp);
        Assert.Equal(1, position.EntryBarIndex);
    }

    // ── §26: horizon boundary (exit is HorizonBars after the FILL bar, never after the signal bar) ────

    [Fact]
    public void Horizon_ExitIsExactlyAtFillPlusHorizon_BarBeyondItNeverInfluencesTheResult()
    {
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m), FillBar(5, 100m) };
        for (int j = 1; j <= 10; j++)
            bars.Add(Bar((j + 1) * 5, 101m, 99m, 100m));
        bars.Add(Bar(65, high: 1000m, low: 1m, close: 500m)); // bar 12: enormous, must never be read

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m), ExecutionConfiguration.Create(10));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(11, position.ExitBarIndex);
        Assert.Equal(100m, position.ExitPrice); // bar 11's Close, unaffected by bar 12
    }

    // ── §18: insufficient future data - exit bar does not exist (fill bar DOES exist) ──────────────────

    [Fact]
    public void InsufficientFutureData_ExitBarMissing_NeverProducesAClosedPosition_NeverTruncatesTheHorizon()
    {
        var bars = new List<HistoricalBar>
        {
            Bar(0, 100m, 100m, 100m), Bar(5, 100m, 100m, 100m), Bar(10, 100m, 100m, 100m),
            Bar(15, 100m, 100m, 100m), Bar(20, 100m, 100m, 100m) // signal at index 3; fill bar (index 4) exists, exit bar (index 14) does not
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(3, DirectionCandidate.BUY_CANDIDATE, 100m), ExecutionConfiguration.Create(10));

        Assert.Equal(PositionStatus.InsufficientFutureData, position.Status);
        Assert.Equal(4, position.EntryBarIndex); // the fill bar was resolved before the exit-bar check failed
        Assert.Equal(100m, position.EntryPrice);
        Assert.Null(position.ExitPrice);
        Assert.Null(position.ExitTimestamp);
        Assert.Null(position.ExitBarIndex);
        Assert.Null(position.ExitReason);
        Assert.Null(position.HoldingBars);
        Assert.Null(position.GrossPriceMove);
        Assert.Null(position.Return);
        Assert.False(string.IsNullOrWhiteSpace(position.Reason));
    }

    // ── P0-1 (brief, explicit requirement): fill bar itself does not exist (signal at the last bar) ────

    [Fact]
    public void InsufficientFutureData_FillBarMissing_SignalAtLastBar_NeverFabricatesAFill()
    {
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m), Bar(5, 100m, 100m, 100m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(1, DirectionCandidate.BUY_CANDIDATE, 100m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.InsufficientFutureData, position.Status);
        Assert.Equal(1, position.EntryBarIndex); // still the signal bar - no fill bar was ever resolved
        Assert.Null(position.EntryPrice);
        Assert.Null(position.ExitPrice);
        Assert.Null(position.HoldingBars);
        Assert.Null(position.Return);
        Assert.False(string.IsNullOrWhiteSpace(position.Reason));
    }

    // ── §21: invalid exit bar (fill bar is valid; the EXIT bar is the invalid one) ──────────────────────

    [Fact]
    public void InvalidExitBar_HighBelowLow_IsRejectedExplicitly_NeverComputedSilently()
    {
        var bars = new List<HistoricalBar>
        {
            Bar(0, 100m, 100m, 100m),
            FillBar(5, 100m),
            new HistoricalBar(Anchor.AddMinutes(10), Open: 100m, High: 90m, Low: 95m, Close: 100m, Volume: 10m) // High<Low
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.InvalidExit, position.Status);
        Assert.Equal(100m, position.EntryPrice); // the fill bar was valid - only the exit bar is rejected
        Assert.Null(position.ExitPrice);
        Assert.False(string.IsNullOrWhiteSpace(position.Reason));
    }

    // ── P0-1: invalid FILL bar (the bar right after the signal itself fails Validate()) ─────────────────

    [Fact]
    public void InvalidFillBar_HighBelowLow_IsRejectedAsInvalidEntry_NeverComputedSilently()
    {
        var bars = new List<HistoricalBar>
        {
            Bar(0, 100m, 100m, 100m),
            new HistoricalBar(Anchor.AddMinutes(5), Open: 100m, High: 90m, Low: 95m, Close: 100m, Volume: 10m), // High<Low
            Bar(10, 101m, 99m, 100m)
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.InvalidEntry, position.Status);
        Assert.Null(position.EntryPrice);
        Assert.False(string.IsNullOrWhiteSpace(position.Reason));
    }

    // ── §20: invalid entry (signal-bar reference price, checked before any bar is even indexed) ────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void InvalidEntryPrice_NeverDividesByZero_ReportsInvalidEntryStatus(double invalidEntry)
    {
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m), Bar(5, 101m, 99m, 100m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, (decimal)invalidEntry), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.InvalidEntry, position.Status);
        Assert.Null(position.ExitPrice);
        Assert.False(string.IsNullOrWhiteSpace(position.Reason));
    }

    [Fact]
    public void MissingEntryPrice_ReportsInvalidEntryStatus()
    {
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m), Bar(5, 101m, 99m, 100m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, null), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.InvalidEntry, position.Status);
    }

    // ── §19/§27 in DirectionCandidate.WATCH too: NO_ACTION/WATCH never create a position ────────────

    [Theory]
    [InlineData(DirectionCandidateKindAll.NoAction)]
    [InlineData(DirectionCandidateKindAll.Watch)]
    public void NonDirectional_NeverCreatesAPosition_NeverFabricatesAnEntryPrice(DirectionCandidateKindAll kind)
    {
        DirectionCandidate direction = kind == DirectionCandidateKindAll.NoAction ? DirectionCandidate.NO_ACTION : DirectionCandidate.WATCH;
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m), Bar(5, 101m, 99m, 100m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, direction, null), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.NotExecutable, position.Status);
        Assert.Null(position.EntryPrice);
        Assert.Null(position.ExitPrice);
        Assert.Null(position.GrossPriceMove);
        Assert.Null(position.Return);
    }

    public enum DirectionCandidateKindAll { NoAction, Watch }

    // ── Argument validation ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExecutionConfiguration_RejectsNonPositiveHorizon()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExecutionConfiguration.Create(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExecutionConfiguration.Create(-1));
    }

    // ── §6/§2: SimulatedPosition is an immutable record - PositionId is deterministic (bar index) ────

    [Fact]
    public void SimulatedPosition_PositionId_IsTheSignalBarIndex_NeverARandomIdentifier()
    {
        var bars = new List<HistoricalBar>();
        for (int i = 0; i <= 6; i++)
            bars.Add(Bar(i * 5, 101m, 99m, 100m));

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(4, DirectionCandidate.BUY_CANDIDATE, 100m), ExecutionConfiguration.Create(1));

        // Note: PositionId equals the CANDIDATE's SignalBarIndex, independent of where in `bars` it sits -
        // deliberately not re-derived from the bar list itself.
        Assert.Equal(4, position.PositionId);
        Assert.Equal(PositionStatus.Closed, position.Status);
    }

    [Fact]
    public void SimulatedPosition_IsARecord_StructurallyEqualPositionsAreEqual()
    {
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m), FillBar(5, 100m), Bar(10, 101m, 99m, 100m) };
        ExecutionConfiguration config = ExecutionConfiguration.Create(1);
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m);

        SimulatedPosition first = ExecutionSimulator.SimulateCore(bars, candidate, config);
        SimulatedPosition second = ExecutionSimulator.SimulateCore(bars, candidate, config);

        Assert.Equal(first, second);
    }
}
