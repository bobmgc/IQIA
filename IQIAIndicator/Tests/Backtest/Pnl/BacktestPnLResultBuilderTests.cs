using System;
using System.Collections.Generic;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Engine.EntryTrigger;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Pnl;

/// <summary>
/// Sprint 15.25 (Lot 14.6, brief §16/§17/§18/§24/§32-§38). <see cref="BacktestPnLResultBuilder"/> - the
/// equity curve, drawdown, worked examples from the brief itself, median, empty input, and BUY/SELL
/// separation.
/// </summary>
public sealed class BacktestPnLResultBuilderTests
{
    private static readonly DateTime Anchor = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    private static PositionPnLResult ClosedPnl(int id, DateTime exitTs, DirectionCandidate direction, decimal grossPnL) =>
        new(id, PositionStatus.Closed, direction, Anchor, 100m, exitTs, 100m, 10, 0m, 0.0, grossPnL, "USD", 1, ExitReason.TimeHorizon);

    private static PositionPnLResult NotClosed(int id, PositionStatus status) =>
        new(id, status, DirectionCandidate.NO_ACTION, Anchor, null, null, null, null, null, null, null, "USD", 1, null);

    // ── §17: MaximumDrawdown worked example from the brief itself ──────────────────────────────────

    [Fact]
    public void MaximumDrawdown_MatchesTheBriefsOwnWorkedExample()
    {
        // Equity sequence in the brief: 100000, 101000, 99000, 98000, 103000 -> MaximumDrawdown = -3000.
        // Expressed here as StartingCapital=100000 plus four closing positions.
        var positions = new List<PositionPnLResult>
        {
            ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 1000m),   // -> 101000
            ClosedPnl(2, Anchor.AddMinutes(10), DirectionCandidate.BUY_CANDIDATE, -2000m), // -> 99000
            ClosedPnl(3, Anchor.AddMinutes(15), DirectionCandidate.BUY_CANDIDATE, -1000m), // -> 98000
            ClosedPnl(4, Anchor.AddMinutes(20), DirectionCandidate.BUY_CANDIDATE, 5000m),  // -> 103000
        };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, startingCapital: 100000m);

        Assert.Equal(-3000m, result.MaximumDrawdown);
        Assert.Equal(103000m, result.FinalEquity);
        Assert.Equal(3000m, result.FinalGrossPnL);
    }

    // ── §35: single win ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SingleWin_FinalEquityAndZeroDrawdown_MatchTheBrief()
    {
        var positions = new List<PositionPnLResult> { ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 500m) };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, startingCapital: 100000m);

        Assert.Equal(100500m, result.FinalEquity);
        Assert.Equal(0m, result.MaximumDrawdown);
        Assert.Equal(0m, result.EquityCurve[0].Drawdown);
    }

    // ── §36: single loss ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SingleLoss_FinalEquityAndMaximumDrawdown_MatchTheBrief()
    {
        var positions = new List<PositionPnLResult> { ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.SELL_CANDIDATE, -500m) };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, startingCapital: 100000m);

        Assert.Equal(99500m, result.FinalEquity);
        Assert.Equal(-500m, result.MaximumDrawdown);
    }

    // ── §37: win then loss ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void WinThenLoss_EquitySequenceAndMaximumDrawdown_MatchTheBrief()
    {
        var positions = new List<PositionPnLResult>
        {
            ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 500m),
            ClosedPnl(2, Anchor.AddMinutes(10), DirectionCandidate.BUY_CANDIDATE, -300m),
        };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, startingCapital: 100000m);

        Assert.Equal(100500m, result.EquityCurve[0].Equity);
        Assert.Equal(100200m, result.EquityCurve[1].Equity);
        Assert.Equal(-300m, result.MaximumDrawdown);
    }

    // ── §38: loss then win ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LossThenWin_EquitySequenceAndMaximumDrawdown_MatchTheBrief()
    {
        var positions = new List<PositionPnLResult>
        {
            ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.SELL_CANDIDATE, -500m),
            ClosedPnl(2, Anchor.AddMinutes(10), DirectionCandidate.BUY_CANDIDATE, 1000m),
        };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, startingCapital: 100000m);

        Assert.Equal(99500m, result.EquityCurve[0].Equity);
        Assert.Equal(100500m, result.EquityCurve[1].Equity);
        Assert.Equal(-500m, result.MaximumDrawdown);
    }

    // ── §18: drawdown edge cases ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoPositions_ProducesZeroEverything_NeverThrows()
    {
        BacktestPnLResult result = BacktestPnLResultBuilder.Build(new List<PositionPnLResult>(), startingCapital: 100000m);

        Assert.Equal(0, result.Summary.PositionCount);
        Assert.Equal(0m, result.FinalGrossPnL);
        Assert.Equal(0m, result.MaximumDrawdown);
        Assert.Empty(result.EquityCurve);
    }

    [Fact]
    public void ReturnToPreviousPeak_DrawdownIsZeroAgain()
    {
        var positions = new List<PositionPnLResult>
        {
            ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 1000m),
            ClosedPnl(2, Anchor.AddMinutes(10), DirectionCandidate.BUY_CANDIDATE, -1000m),
            ClosedPnl(3, Anchor.AddMinutes(15), DirectionCandidate.BUY_CANDIDATE, 1000m), // back to the same peak
        };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, null);

        Assert.Equal(0m, result.EquityCurve[2].Drawdown);
    }

    [Fact]
    public void NewPeak_DrawdownIsZero_MaximumDrawdownReflectsOnlyTheDipBefore()
    {
        var positions = new List<PositionPnLResult>
        {
            ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 1000m),
            ClosedPnl(2, Anchor.AddMinutes(10), DirectionCandidate.BUY_CANDIDATE, -500m),
            ClosedPnl(3, Anchor.AddMinutes(15), DirectionCandidate.BUY_CANDIDATE, 2000m), // new peak
        };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, null);

        Assert.Equal(0m, result.EquityCurve[2].Drawdown);
        Assert.Equal(-500m, result.MaximumDrawdown);
    }

    [Fact]
    public void SeveralConsecutiveLosses_MaximumDrawdownAccumulates()
    {
        var positions = new List<PositionPnLResult>
        {
            ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, -100m),
            ClosedPnl(2, Anchor.AddMinutes(10), DirectionCandidate.BUY_CANDIDATE, -200m),
            ClosedPnl(3, Anchor.AddMinutes(15), DirectionCandidate.BUY_CANDIDATE, -300m),
        };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, null);

        Assert.Equal(-600m, result.MaximumDrawdown);
    }

    [Fact]
    public void FlatPnl_DrawdownIsZero()
    {
        var positions = new List<PositionPnLResult>
        {
            ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 0m),
            ClosedPnl(2, Anchor.AddMinutes(10), DirectionCandidate.BUY_CANDIDATE, 0m),
        };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, null);

        Assert.Equal(0m, result.MaximumDrawdown);
    }

    // ── §12: CumulativePnL without StartingCapital never fabricates a capital level ─────────────────

    [Fact]
    public void NoStartingCapital_ProducesCumulativePnLButNeverAFabricatedEquity()
    {
        var positions = new List<PositionPnLResult> { ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 500m) };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, startingCapital: null);

        Assert.Equal(500m, result.EquityCurve[0].CumulativePnL);
        Assert.Null(result.EquityCurve[0].Equity);
        Assert.Null(result.EquityCurve[0].DrawdownPercent);
        Assert.Null(result.FinalEquity);
        // Drawdown itself is still computed (offset-invariant) - see EquityPoint's doc comment.
        Assert.Equal(0m, result.EquityCurve[0].Drawdown);
    }

    // ── §13: deterministic ordering (ExitTimestamp, then PositionId tie-break) ─────────────────────

    [Fact]
    public void EquityCurve_IsOrderedByExitTimestamp_ThenPositionIdAsATieBreak()
    {
        var positions = new List<PositionPnLResult>
        {
            ClosedPnl(3, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 10m),
            ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 20m), // same timestamp, lower id
            ClosedPnl(2, Anchor.AddMinutes(1), DirectionCandidate.BUY_CANDIDATE, 30m), // earliest
        };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, null);

        Assert.Equal(2, result.EquityCurve[0].PositionId);
        Assert.Equal(1, result.EquityCurve[1].PositionId); // tie broken by PositionId, not input order
        Assert.Equal(3, result.EquityCurve[2].PositionId);
    }

    // ── §19: BUY/SELL never lost in the ALL aggregation ─────────────────────────────────────────────

    [Fact]
    public void BuyAndSellSummaries_AreSeparateFromAll_AndFromEachOther()
    {
        var positions = new List<PositionPnLResult>
        {
            ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 100m),
            ClosedPnl(2, Anchor.AddMinutes(10), DirectionCandidate.BUY_CANDIDATE, -40m),
            ClosedPnl(3, Anchor.AddMinutes(15), DirectionCandidate.SELL_CANDIDATE, 200m),
        };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, null);

        Assert.Equal(3, result.Summary.ClosedCount);
        Assert.Equal(2, result.BuySummary.ClosedCount);
        Assert.Equal(1, result.SellSummary.ClosedCount);
        Assert.Equal(60m, result.BuySummary.NetGrossPnL);
        Assert.Equal(200m, result.SellSummary.NetGrossPnL);
        Assert.Equal(260m, result.Summary.NetGrossPnL);
    }

    // ── §21: GrossProfit/GrossLoss - signed, never absolute-valued ──────────────────────────────────

    [Fact]
    public void GrossProfitAndGrossLoss_MatchTheBriefsExample()
    {
        // +100, -40, -20 -> GrossProfit=+100, GrossLoss=-60, NetGrossPnL=+40
        var positions = new List<PositionPnLResult>
        {
            ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 100m),
            ClosedPnl(2, Anchor.AddMinutes(10), DirectionCandidate.BUY_CANDIDATE, -40m),
            ClosedPnl(3, Anchor.AddMinutes(15), DirectionCandidate.BUY_CANDIDATE, -20m),
        };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, null);

        Assert.Equal(100m, result.Summary.GrossProfit);
        Assert.Equal(-60m, result.Summary.GrossLoss);
        Assert.Equal(40m, result.Summary.NetGrossPnL);
    }

    // ── §22: WinRate never divides by zero ──────────────────────────────────────────────────────────

    [Fact]
    public void WinRate_NoClosedPositions_IsNullNeverADivisionByZero()
    {
        var positions = new List<PositionPnLResult> { NotClosed(1, PositionStatus.NotExecutable) };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, null);

        Assert.Null(result.Summary.WinRate);
        Assert.Null(result.Summary.AveragePnL);
    }

    [Fact]
    public void WinRate_MatchesWinningOverClosed()
    {
        var positions = new List<PositionPnLResult>
        {
            ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 10m),
            ClosedPnl(2, Anchor.AddMinutes(10), DirectionCandidate.BUY_CANDIDATE, 10m),
            ClosedPnl(3, Anchor.AddMinutes(15), DirectionCandidate.BUY_CANDIDATE, -10m),
            ClosedPnl(4, Anchor.AddMinutes(20), DirectionCandidate.BUY_CANDIDATE, -10m),
        };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, null);

        Assert.Equal(0.5, result.Summary.WinRate);
        Assert.Equal(2, result.Summary.WinningPositions);
        Assert.Equal(2, result.Summary.LosingPositions);
    }

    [Fact]
    public void BreakEvenPosition_IsNeitherWinningNorLosing_ButStillCountsTowardMedianAndAverage()
    {
        var positions = new List<PositionPnLResult>
        {
            ClosedPnl(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 10m),
            ClosedPnl(2, Anchor.AddMinutes(10), DirectionCandidate.BUY_CANDIDATE, 0m),
            ClosedPnl(3, Anchor.AddMinutes(15), DirectionCandidate.BUY_CANDIDATE, -10m),
        };

        BacktestPnLResult result = BacktestPnLResultBuilder.Build(positions, null);

        Assert.Equal(1, result.Summary.WinningPositions);
        Assert.Equal(1, result.Summary.LosingPositions);
        Assert.Equal(3, result.Summary.ClosedCount);
        Assert.Equal(0m, result.Summary.MedianPnL);
    }

    // ── §24: median convention (decimal-native) ─────────────────────────────────────────────────────

    [Fact]
    public void Median_OddCount_ReturnsTheMiddleElement()
    {
        Assert.Equal(2m, BacktestPnLResultBuilder.Median(new List<decimal> { 3m, 1m, 2m }));
    }

    [Fact]
    public void Median_EvenCount_ReturnsTheAverageOfTheTwoMiddleElements()
    {
        Assert.Equal(2.5m, BacktestPnLResultBuilder.Median(new List<decimal> { 1m, 2m, 3m, 4m }));
    }

    [Fact]
    public void Median_EmptyInput_ReturnsNullNeverZero()
    {
        Assert.Null(BacktestPnLResultBuilder.Median(new List<decimal>()));
    }

    // ── §43: rebuilding with an invalid StartingCapital throws ──────────────────────────────────────

    [Fact]
    public void Build_RejectsNonPositiveStartingCapital()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BacktestPnLResultBuilder.Build(new List<PositionPnLResult>(), 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => BacktestPnLResultBuilder.Build(new List<PositionPnLResult>(), -1m));
    }
}
