using System;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Engine.EntryTrigger;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Pnl;

/// <summary>
/// Sprint 15.25 (Lot 14.6, brief §7/§39/§40/§41/§42/§43). <see cref="PositionPnLCalculator"/> - the
/// GrossPnL formula, Return pass-through (never recomputed), instrument-agnosticism (MES/ES fixtures
/// only, never an <c>if (symbol)</c> branch in the engine), and quantity proportionality.
/// </summary>
public sealed class PositionPnLCalculatorTests
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

    // ── §40: MES fixture ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Mes_Buy_TwoPointMove_ProducesTenDollarGrossPnL()
    {
        InstrumentPnLSpecification mes = InstrumentPnLSpecification.Create("MES", priceUnitValue: 5m, currency: "USD");
        PnLConfiguration config = PnLConfiguration.Create(mes, quantity: 1);

        PositionPnLResult result = PositionPnLCalculator.Calculate(Closed(DirectionCandidate.BUY_CANDIDATE, 6500m, 6502m), config);

        Assert.Equal(2m, result.GrossPriceMove);
        Assert.Equal(10m, result.GrossPnL);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void Mes_Sell_TwoPointFavorableMove_ProducesTenDollarGrossPnL()
    {
        InstrumentPnLSpecification mes = InstrumentPnLSpecification.Create("MES", priceUnitValue: 5m, currency: "USD");
        PnLConfiguration config = PnLConfiguration.Create(mes, quantity: 1);

        PositionPnLResult result = PositionPnLCalculator.Calculate(Closed(DirectionCandidate.SELL_CANDIDATE, 6500m, 6498m), config);

        Assert.Equal(2m, result.GrossPriceMove);
        Assert.Equal(10m, result.GrossPnL);
    }

    // ── §41: ES fixture - same price move, different instrument, no hardcoded branch ────────────────

    [Fact]
    public void Es_SameTwoPointMove_ProducesOneHundredDollarGrossPnL()
    {
        InstrumentPnLSpecification es = InstrumentPnLSpecification.Create("ES", priceUnitValue: 50m, currency: "USD");
        PnLConfiguration config = PnLConfiguration.Create(es, quantity: 1);

        PositionPnLResult result = PositionPnLCalculator.Calculate(Closed(DirectionCandidate.BUY_CANDIDATE, 6500m, 6502m), config);

        Assert.Equal(100m, result.GrossPnL);
    }

    // ── §42: quantity proportionality ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 10.0)]
    [InlineData(2, 20.0)]
    [InlineData(5, 50.0)]
    public void GrossPnL_IsExactlyProportionalToQuantity(int quantity, double expectedGrossPnL)
    {
        InstrumentPnLSpecification mes = InstrumentPnLSpecification.Create("MES", 5m, "USD");
        PnLConfiguration config = PnLConfiguration.Create(mes, quantity);

        PositionPnLResult result = PositionPnLCalculator.Calculate(Closed(DirectionCandidate.BUY_CANDIDATE, 6500m, 6502m), config);

        Assert.Equal((decimal)expectedGrossPnL, result.GrossPnL);
    }

    // ── §8/§39: Return coherence - never recomputed ─────────────────────────────────────────────────

    [Fact]
    public void Return_IsCopiedVerbatimFromTheSimulatedPosition_NeverRecomputed()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m, 105m);
        InstrumentPnLSpecification mes = InstrumentPnLSpecification.Create("MES", 5m, "USD");
        PnLConfiguration config = PnLConfiguration.Create(mes);

        PositionPnLResult result = PositionPnLCalculator.Calculate(position, config);

        Assert.Equal(position.Return, result.Return);
    }

    // ── Non-Closed positions: GrossPnL stays null, never fabricated ─────────────────────────────────

    [Theory]
    [InlineData(PositionStatus.NotExecutable)]
    [InlineData(PositionStatus.InvalidEntry)]
    [InlineData(PositionStatus.InsufficientFutureData)]
    [InlineData(PositionStatus.InvalidExit)]
    [InlineData(PositionStatus.InvalidStopTarget)] // Sprint 15.25 (Lot 15.4): same generic Status != Closed guard covers this new status too.
    public void NonClosedPosition_NeverProducesAGrossPnL(PositionStatus status)
    {
        var position = new SimulatedPosition(
            1, status, "not closed", DirectionCandidate.BUY_CANDIDATE, EntryTs, null, 0,
            null, null, null, null, null, null, null);
        InstrumentPnLSpecification mes = InstrumentPnLSpecification.Create("MES", 5m, "USD");
        PnLConfiguration config = PnLConfiguration.Create(mes);

        PositionPnLResult result = PositionPnLCalculator.Calculate(position, config);

        Assert.Equal(status, result.Status);
        Assert.Null(result.GrossPnL);
    }

    // ── §43: invalid configuration is rejected, never silently produces a wrong P&L ─────────────────

    [Fact]
    public void InstrumentPnLSpecification_RejectsNonPositivePriceUnitValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InstrumentPnLSpecification.Create("MES", 0m, "USD"));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstrumentPnLSpecification.Create("MES", -5m, "USD"));
    }

    [Fact]
    public void InstrumentPnLSpecification_RejectsEmptyCurrency()
    {
        Assert.Throws<ArgumentException>(() => InstrumentPnLSpecification.Create("MES", 5m, ""));
        Assert.Throws<ArgumentException>(() => InstrumentPnLSpecification.Create("MES", 5m, "   "));
    }

    [Fact]
    public void InstrumentPnLSpecification_RejectsEmptySymbol()
    {
        Assert.Throws<ArgumentException>(() => InstrumentPnLSpecification.Create("", 5m, "USD"));
    }

    [Fact]
    public void PnLConfiguration_RejectsNonPositiveQuantity()
    {
        InstrumentPnLSpecification mes = InstrumentPnLSpecification.Create("MES", 5m, "USD");
        Assert.Throws<ArgumentOutOfRangeException>(() => PnLConfiguration.Create(mes, quantity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PnLConfiguration.Create(mes, quantity: -1));
    }

    [Fact]
    public void PnLConfiguration_RejectsNonPositiveStartingCapital()
    {
        InstrumentPnLSpecification mes = InstrumentPnLSpecification.Create("MES", 5m, "USD");
        Assert.Throws<ArgumentOutOfRangeException>(() => PnLConfiguration.Create(mes, startingCapital: 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => PnLConfiguration.Create(mes, startingCapital: -100m));
    }

    [Fact]
    public void PnLConfiguration_DefaultQuantity_IsOneTheoreticalUnit()
    {
        InstrumentPnLSpecification mes = InstrumentPnLSpecification.Create("MES", 5m, "USD");
        PnLConfiguration config = PnLConfiguration.Create(mes);
        Assert.Equal(1, config.Quantity);
        Assert.Null(config.StartingCapital);
    }
}
