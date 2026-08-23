using System;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Engine.EntryTrigger;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §3/§5/§6/§11/§12). <see cref="ExecutionPriceModel"/> - the
/// Signal-Price -&gt; Executed-Price step, in isolation from cost/quantity dollar conversion (that's
/// <see cref="PositionCostCalculator"/>'s job).
/// </summary>
public sealed class ExecutionPriceModelTests
{
    private static readonly DateTime Ts = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    // ── §5: zero slippage, zero spread -> executed == theoretical ──────────────────────────────────

    [Theory]
    [InlineData(OrderSide.Buy)]
    [InlineData(OrderSide.Sell)]
    public void ZeroSlippageAndZeroSpread_ExecutedPriceEqualsTheoretical(OrderSide side)
    {
        ExecutedPrice fill = ExecutionPriceModel.Apply(100m, side, 1, Ts, SlippageConfiguration.None(), SpreadConfiguration.None());

        Assert.Equal(100m, fill.Price);
        Assert.Equal(0m, fill.SlippageApplied);
        Assert.Equal(0m, fill.SpreadApplied);
    }

    // ── §5: positive slippage, direction of the adjustment ──────────────────────────────────────────

    [Fact]
    public void PositiveSlippage_Buy_AddsToThePrice()
    {
        ExecutedPrice fill = ExecutionPriceModel.Apply(100m, OrderSide.Buy, 1, Ts, SlippageConfiguration.Fixed(0.25m), SpreadConfiguration.None());
        Assert.Equal(100.25m, fill.Price);
    }

    [Fact]
    public void PositiveSlippage_Sell_SubtractsFromThePrice()
    {
        ExecutedPrice fill = ExecutionPriceModel.Apply(100m, OrderSide.Sell, 1, Ts, SlippageConfiguration.Fixed(0.25m), SpreadConfiguration.None());
        Assert.Equal(99.75m, fill.Price);
    }

    // ── §6: symmetric spread - BUY uses ASK, SELL uses BID ──────────────────────────────────────────

    [Fact]
    public void Spread_Buy_ExecutesAtTheAsk_MidPlusHalfSpread()
    {
        ExecutedPrice fill = ExecutionPriceModel.Apply(100m, OrderSide.Buy, 1, Ts, SlippageConfiguration.None(), SpreadConfiguration.Fixed(1m));
        Assert.Equal(100.5m, fill.Price);
        Assert.Equal(0.5m, fill.SpreadApplied);
    }

    [Fact]
    public void Spread_Sell_ExecutesAtTheBid_MidMinusHalfSpread()
    {
        ExecutedPrice fill = ExecutionPriceModel.Apply(100m, OrderSide.Sell, 1, Ts, SlippageConfiguration.None(), SpreadConfiguration.Fixed(1m));
        Assert.Equal(99.5m, fill.Price);
        Assert.Equal(0.5m, fill.SpreadApplied);
    }

    [Fact]
    public void ZeroSpread_ExecutesExactlyAtMid()
    {
        ExecutedPrice buy = ExecutionPriceModel.Apply(100m, OrderSide.Buy, 1, Ts, SlippageConfiguration.None(), SpreadConfiguration.None());
        ExecutedPrice sell = ExecutionPriceModel.Apply(100m, OrderSide.Sell, 1, Ts, SlippageConfiguration.None(), SpreadConfiguration.None());

        Assert.Equal(100m, buy.Price);
        Assert.Equal(100m, sell.Price);
    }

    // ── combined slippage + spread compose additively, in the same adverse direction ────────────────

    [Fact]
    public void SlippageAndSpread_Combine_Buy()
    {
        ExecutedPrice fill = ExecutionPriceModel.Apply(100m, OrderSide.Buy, 1, Ts, SlippageConfiguration.Fixed(0.25m), SpreadConfiguration.Fixed(0.5m));
        Assert.Equal(100.5m, fill.Price); // +0.25 (half-spread) + 0.25 (slippage)
    }

    [Fact]
    public void SlippageAndSpread_Combine_Sell()
    {
        ExecutedPrice fill = ExecutionPriceModel.Apply(100m, OrderSide.Sell, 1, Ts, SlippageConfiguration.Fixed(0.25m), SpreadConfiguration.Fixed(0.5m));
        Assert.Equal(99.5m, fill.Price);
    }

    // ── quantity > 1: the unit PRICE never depends on quantity (only downstream $ cost does) ────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void ExecutedPrice_IsIndependentOfQuantity(int quantity)
    {
        ExecutedPrice fill = ExecutionPriceModel.Apply(100m, OrderSide.Buy, quantity, Ts, SlippageConfiguration.Fixed(0.25m), SpreadConfiguration.Fixed(0.5m));

        Assert.Equal(100.5m, fill.Price);
        Assert.Equal(quantity, fill.Quantity);
    }

    // ── fill-side resolution from a position's direction ────────────────────────────────────────────

    [Fact]
    public void EntryFillSide_Long_IsBuy_Short_IsSell()
    {
        Assert.Equal(OrderSide.Buy, ExecutionPriceModel.EntryFillSide(DirectionCandidate.BUY_CANDIDATE));
        Assert.Equal(OrderSide.Sell, ExecutionPriceModel.EntryFillSide(DirectionCandidate.SELL_CANDIDATE));
    }

    [Fact]
    public void ExitFillSide_Long_IsSell_Short_IsBuy()
    {
        Assert.Equal(OrderSide.Sell, ExecutionPriceModel.ExitFillSide(DirectionCandidate.BUY_CANDIDATE));
        Assert.Equal(OrderSide.Buy, ExecutionPriceModel.ExitFillSide(DirectionCandidate.SELL_CANDIDATE));
    }

    [Theory]
    [InlineData(DirectionCandidate.WATCH)]
    [InlineData(DirectionCandidate.NO_ACTION)]
    public void FillSideResolution_RejectsNonDirectionalCandidates(DirectionCandidate direction)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExecutionPriceModel.EntryFillSide(direction));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExecutionPriceModel.ExitFillSide(direction));
    }

    // ── determinism ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SameInputs_ProduceTheExactSameExecutedPrice()
    {
        ExecutedPrice first = ExecutionPriceModel.Apply(6500m, OrderSide.Buy, 3, Ts, SlippageConfiguration.Fixed(0.25m), SpreadConfiguration.Fixed(0.5m));
        ExecutedPrice second = ExecutionPriceModel.Apply(6500m, OrderSide.Buy, 3, Ts, SlippageConfiguration.Fixed(0.25m), SpreadConfiguration.Fixed(0.5m));

        Assert.Equal(first, second);
    }
}
