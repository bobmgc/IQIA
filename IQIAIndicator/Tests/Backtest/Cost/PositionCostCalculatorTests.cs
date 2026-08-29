using System;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Engine.EntryTrigger;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §7/§8/§12). <see cref="PositionCostCalculator"/> - commission, fees,
/// combined costs, Gross/Net PnL, Long/Short worked examples, quantity scaling, zero-cost equivalence,
/// and non-Closed positions.
/// </summary>
public sealed class PositionCostCalculatorTests
{
    private static readonly DateTime EntryTs = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime ExitTs = new(2026, 1, 5, 15, 20, 0, DateTimeKind.Utc);

    private static SimulatedPosition Closed(DirectionCandidate direction, decimal entry, decimal exit)
    {
        decimal move = direction == DirectionCandidate.BUY_CANDIDATE ? exit - entry : entry - exit;
        double entryD = (double)entry, exitD = (double)exit;
        double ret = direction == DirectionCandidate.BUY_CANDIDATE ? (exitD - entryD) / entryD : (entryD - exitD) / entryD;

        return new SimulatedPosition(
            PositionId: 1, PositionStatus.Closed, null, direction,
            EntryTs, entry, EntryBarIndex: 0,
            ExitTs, exit, ExitBarIndex: 10, ExitReason.TimeHorizon,
            HoldingBars: 10, GrossPriceMove: move, Return: ret);
    }

    private static PnLConfiguration Pnl(int quantity = 1) =>
        PnLConfiguration.Create(InstrumentPnLSpecification.Create("MES", priceUnitValue: 5m, currency: "USD"), quantity: quantity);

    private static PositionPnLResult PnlResultFor(SimulatedPosition position, PnLConfiguration config) =>
        PositionPnLCalculator.Calculate(position, config);

    // ── §7: commission ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Commission_Zero_ProducesNoCommissionCost()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m, 110m);
        PnLConfiguration pnl = Pnl();
        ExecutionCostConfiguration cost = ExecutionCostConfiguration.Create(enabled: true, commission: CommissionConfiguration.None());

        PositionCostResult result = PositionCostCalculator.Calculate(position, PnlResultFor(position, pnl), pnl, cost);

        Assert.Equal(0m, result.Cost!.Commission);
    }

    [Fact]
    public void Commission_FixedPerOrder_IsChargedOnBothEntryAndExit()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m, 110m);
        PnLConfiguration pnl = Pnl();
        ExecutionCostConfiguration cost = ExecutionCostConfiguration.Create(enabled: true, commission: CommissionConfiguration.Create(perOrder: 2.00m));

        PositionCostResult result = PositionCostCalculator.Calculate(position, PnlResultFor(position, pnl), pnl, cost);

        Assert.Equal(4.00m, result.Cost!.Commission); // $2 entry + $2 exit
    }

    [Fact]
    public void Commission_PerUnit_ScalesWithQuantityOnBothLegs()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m, 110m);
        PnLConfiguration pnl = Pnl(quantity: 3);
        ExecutionCostConfiguration cost = ExecutionCostConfiguration.Create(enabled: true, commission: CommissionConfiguration.Create(perUnit: 0.50m));

        PositionCostResult result = PositionCostCalculator.Calculate(position, PnlResultFor(position, pnl), pnl, cost);

        Assert.Equal(3.00m, result.Cost!.Commission); // 2 legs * 3 contracts * $0.50
    }

    // ── §7: fees ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Fees_Zero_ProducesNoFeesCost()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m, 110m);
        PnLConfiguration pnl = Pnl();
        ExecutionCostConfiguration cost = ExecutionCostConfiguration.Create(enabled: true, fees: FeesConfiguration.None());

        PositionCostResult result = PositionCostCalculator.Calculate(position, PnlResultFor(position, pnl), pnl, cost);

        Assert.Equal(0m, result.Cost!.Fees);
    }

    [Fact]
    public void Fees_Fixed_IsChargedOnBothEntryAndExit()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m, 110m);
        PnLConfiguration pnl = Pnl();
        ExecutionCostConfiguration cost = ExecutionCostConfiguration.Create(enabled: true, fees: FeesConfiguration.Create(0.10m));

        PositionCostResult result = PositionCostCalculator.Calculate(position, PnlResultFor(position, pnl), pnl, cost);

        Assert.Equal(0.20m, result.Cost!.Fees);
    }

    // ── §7/§8: combined costs in one trade, full manual calculation ────────────────────────────────

    [Fact]
    public void CombinedCosts_Long_MatchesTheFullManualCalculation()
    {
        // MES-like: $5/point, quantity=1. Slippage=0.25, Spread=0.5 (half=0.25), Commission=$2+$0.50/unit, Fees=$0.10.
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m, 110m);
        PnLConfiguration pnl = Pnl(quantity: 1);
        ExecutionCostConfiguration cost = ExecutionCostConfiguration.Create(
            enabled: true,
            slippage: SlippageConfiguration.Fixed(0.25m),
            spread: SpreadConfiguration.Fixed(0.5m),
            commission: CommissionConfiguration.Create(perOrder: 2.00m, perUnit: 0.50m),
            fees: FeesConfiguration.Create(0.10m));

        PositionPnLResult pnlResult = PnlResultFor(position, pnl);
        PositionCostResult result = PositionCostCalculator.Calculate(position, pnlResult, pnl, cost);

        Assert.Equal(50m, result.GrossPnL);            // 10 pts * $5
        Assert.Equal(2.5m, result.Cost!.SpreadCost);    // 2 legs * 0.25 * $5
        Assert.Equal(2.5m, result.Cost!.SlippageCost);  // 2 legs * 0.25 * $5
        Assert.Equal(5.00m, result.Cost!.Commission);   // 2*2.00 + 2*1*0.50
        Assert.Equal(0.20m, result.Cost!.Fees);         // 2*0.10
        Assert.Equal(10.20m, result.Cost!.TotalCost);
        Assert.Equal(39.80m, result.NetPnL);

        // Executed entry/exit: Buy fill at entry (+0.5), Sell fill at exit (-0.5).
        Assert.Equal(100.5m, result.ExecutedEntryPrice);
        Assert.Equal(109.5m, result.ExecutedExitPrice);

        // Invariant: NetPnL must equal PnL computed from EXECUTED prices, minus Commission+Fees only -
        // proves SpreadCost/SlippageCost are never double-counted against the executed-price move.
        decimal executedMove = result.ExecutedExitPrice!.Value - result.ExecutedEntryPrice!.Value;
        decimal pnlFromExecutedPrices = executedMove * 5m * 1;
        Assert.Equal(result.NetPnL, pnlFromExecutedPrices - result.Cost!.Commission - result.Cost!.Fees);
    }

    // ── §12: Long worked example ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Long_EntryOneHundred_ExitOneTen_WithCosts()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m, 110m);
        PnLConfiguration pnl = Pnl(quantity: 1);
        ExecutionCostConfiguration cost = ExecutionCostConfiguration.Create(
            enabled: true,
            slippage: SlippageConfiguration.Fixed(0.25m),
            spread: SpreadConfiguration.Fixed(0.5m),
            commission: CommissionConfiguration.Create(2.00m, 0.50m),
            fees: FeesConfiguration.Create(0.10m));

        PositionCostResult result = PositionCostCalculator.Calculate(position, PnlResultFor(position, pnl), pnl, cost);

        Assert.Equal(50m, result.GrossPnL);
        Assert.Equal(39.80m, result.NetPnL);
    }

    // ── §12: Short worked example ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Short_EntryOneTen_ExitOneHundred_WithCosts()
    {
        SimulatedPosition position = Closed(DirectionCandidate.SELL_CANDIDATE, 110m, 100m);
        PnLConfiguration pnl = Pnl(quantity: 1);
        ExecutionCostConfiguration cost = ExecutionCostConfiguration.Create(
            enabled: true,
            slippage: SlippageConfiguration.Fixed(0.25m),
            spread: SpreadConfiguration.Fixed(0.5m),
            commission: CommissionConfiguration.Create(2.00m, 0.50m),
            fees: FeesConfiguration.Create(0.10m));

        PositionCostResult result = PositionCostCalculator.Calculate(position, PnlResultFor(position, pnl), pnl, cost);

        Assert.Equal(50m, result.GrossPnL);
        Assert.Equal(39.80m, result.NetPnL);

        // Executed entry/exit: Sell fill at entry (-0.5), Buy fill at exit (+0.5).
        Assert.Equal(109.5m, result.ExecutedEntryPrice);
        Assert.Equal(100.5m, result.ExecutedExitPrice);
    }

    // ── §12: quantity scaling ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 2.5)]
    [InlineData(2, 5.0)]
    [InlineData(10, 25.0)]
    public void SlippageAndSpreadCost_ScaleLinearlyWithQuantity(int quantity, double expectedEach)
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m, 110m);
        PnLConfiguration pnl = Pnl(quantity);
        ExecutionCostConfiguration cost = ExecutionCostConfiguration.Create(
            enabled: true,
            slippage: SlippageConfiguration.Fixed(0.25m),
            spread: SpreadConfiguration.Fixed(0.5m));

        PositionCostResult result = PositionCostCalculator.Calculate(position, PnlResultFor(position, pnl), pnl, cost);

        Assert.Equal((decimal)expectedEach, result.Cost!.SlippageCost);
        Assert.Equal((decimal)expectedEach, result.Cost!.SpreadCost);
    }

    [Theory]
    [InlineData(1, 1.00)]
    [InlineData(2, 2.00)]
    [InlineData(10, 10.00)]
    public void CommissionPerUnit_ScalesLinearlyWithQuantity_PerOrderComponentDoesNot(int quantity, double expectedPerUnitPortion)
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m, 110m);
        PnLConfiguration pnl = Pnl(quantity);
        ExecutionCostConfiguration cost = ExecutionCostConfiguration.Create(
            enabled: true,
            commission: CommissionConfiguration.Create(perOrder: 2.00m, perUnit: 0.50m));

        PositionCostResult result = PositionCostCalculator.Calculate(position, PnlResultFor(position, pnl), pnl, cost);

        // Commission = 2*perOrder (constant, 4.00) + 2*quantity*perUnit (scales).
        Assert.Equal(4.00m + (decimal)expectedPerUnitPortion, result.Cost!.Commission);
    }

    // ── §8/§9: zero-cost equivalence ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DirectionCandidate.BUY_CANDIDATE)]
    [InlineData(DirectionCandidate.SELL_CANDIDATE)]
    public void Disabled_NetPnLEqualsGrossPnL_AndExecutedPricesEqualTheoretical(DirectionCandidate direction)
    {
        SimulatedPosition position = direction == DirectionCandidate.BUY_CANDIDATE
            ? Closed(direction, 100m, 110m)
            : Closed(direction, 110m, 100m);
        PnLConfiguration pnl = Pnl();
        PositionPnLResult pnlResult = PnlResultFor(position, pnl);

        PositionCostResult result = PositionCostCalculator.Calculate(position, pnlResult, pnl, ExecutionCostConfiguration.Disabled());

        Assert.Equal(pnlResult.GrossPnL, result.NetPnL);
        Assert.Equal(0m, result.Cost!.TotalCost);
        Assert.Equal(result.TheoreticalEntryPrice, result.ExecutedEntryPrice);
        Assert.Equal(result.TheoreticalExitPrice, result.ExecutedExitPrice);
    }

    [Fact]
    public void EnabledButEveryComponentZero_AlsoProducesNetPnLEqualToGrossPnL()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m, 110m);
        PnLConfiguration pnl = Pnl();
        PositionPnLResult pnlResult = PnlResultFor(position, pnl);
        ExecutionCostConfiguration cost = ExecutionCostConfiguration.Create(enabled: true);

        PositionCostResult result = PositionCostCalculator.Calculate(position, pnlResult, pnl, cost);

        Assert.Equal(pnlResult.GrossPnL, result.NetPnL);
        Assert.Equal(0m, result.Cost!.TotalCost);
    }

    // ── non-Closed positions: never a fabricated cost or NetPnL ─────────────────────────────────────

    [Theory]
    [InlineData(PositionStatus.NotExecutable)]
    [InlineData(PositionStatus.InvalidEntry)]
    [InlineData(PositionStatus.InsufficientFutureData)]
    [InlineData(PositionStatus.InvalidExit)]
    [InlineData(PositionStatus.InvalidStopTarget)] // Sprint 15.25 (Lot 15.4): same generic Status != Closed guard covers this new status too.
    public void NonClosedPosition_NeverProducesACostOrNetPnL(PositionStatus status)
    {
        var position = new SimulatedPosition(
            1, status, "not closed", DirectionCandidate.BUY_CANDIDATE, EntryTs, null, 0,
            null, null, null, null, null, null, null);
        PnLConfiguration pnl = Pnl();
        var pnlResult = new PositionPnLResult(
            1, status, DirectionCandidate.BUY_CANDIDATE, EntryTs, null, null, null, null, null, null, null, "USD", 1, null);
        ExecutionCostConfiguration cost = ExecutionCostConfiguration.Create(
            enabled: true, slippage: SlippageConfiguration.Fixed(0.25m), commission: CommissionConfiguration.Create(2m));

        PositionCostResult result = PositionCostCalculator.Calculate(position, pnlResult, pnl, cost);

        Assert.Equal(status, result.Status);
        Assert.Null(result.ExecutedEntryPrice);
        Assert.Null(result.ExecutedExitPrice);
        Assert.Null(result.Cost);
        Assert.Null(result.NetPnL);
    }

    // ── invalid configuration: PnLConfiguration's own validation still holds through this pipeline ──

    [Fact]
    public void PnLConfiguration_StillRejectsNonPositiveQuantity_WhenUsedWithTheCostLayer()
    {
        InstrumentPnLSpecification mes = InstrumentPnLSpecification.Create("MES", 5m, "USD");
        Assert.Throws<ArgumentOutOfRangeException>(() => PnLConfiguration.Create(mes, quantity: 0));
    }

    // ── determinism ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SameInputs_ProduceTheExactSameResult()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m, 110m);
        PnLConfiguration pnl = Pnl();
        PositionPnLResult pnlResult = PnlResultFor(position, pnl);
        ExecutionCostConfiguration cost = ExecutionCostConfiguration.Create(
            enabled: true, slippage: SlippageConfiguration.Fixed(0.25m), spread: SpreadConfiguration.Fixed(0.5m),
            commission: CommissionConfiguration.Create(2m, 0.5m), fees: FeesConfiguration.Create(0.1m));

        PositionCostResult first = PositionCostCalculator.Calculate(position, pnlResult, pnl, cost);
        PositionCostResult second = PositionCostCalculator.Calculate(position, pnlResult, pnl, cost);

        Assert.Equal(first, second);
    }

    // ── CalculateAll: index-alignment guard ──────────────────────────────────────────────────────────

    [Fact]
    public void CalculateAll_RejectsMismatchedListLengths()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m, 110m);
        PnLConfiguration pnl = Pnl();
        PositionPnLResult pnlResult = PnlResultFor(position, pnl);

        Assert.Throws<ArgumentException>(() => PositionCostCalculator.CalculateAll(
            new[] { position, position }, new[] { pnlResult }, pnl, ExecutionCostConfiguration.Disabled()));
    }
}
