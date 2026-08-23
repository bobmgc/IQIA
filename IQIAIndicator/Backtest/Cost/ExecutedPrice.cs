using System;

namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §5/§6). The fill side of ONE order - deliberately distinct from
/// <see cref="Engine.EntryTrigger.DirectionCandidate"/> (which names the POSITION's direction, e.g. a
/// BUY_CANDIDATE long position). A long position's ENTRY is a Buy order but its EXIT is a Sell order (and
/// vice-versa for a short) - <see cref="ExecutionPriceModel.EntryFillSide"/>/<see cref="ExecutionPriceModel.ExitFillSide"/>
/// resolve which is which. Kept as its own tiny enum rather than overloading DirectionCandidate (a frozen
/// Lot-Engine type this lot must not touch).
/// </summary>
public enum OrderSide
{
    Buy,
    Sell
}

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §4 "ExecutionPrice"). One resolved fill: the theoretical price it started
/// from, the executed price actually used, which side it was, how much quantity/timestamp it applies to,
/// and the slippage/spread amounts (in PRICE UNITS, not dollars) that were applied to produce
/// <see cref="Price"/> from <see cref="TheoreticalPrice"/>. Produced exclusively by
/// <see cref="ExecutionPriceModel.Apply"/> - never constructed ad hoc, so the three values always agree:
/// <c>Price == TheoreticalPrice +/- (SpreadApplied + SlippageApplied)</c>.
/// </summary>
public sealed record ExecutedPrice(
    decimal TheoreticalPrice,
    decimal Price,
    OrderSide Side,
    int Quantity,
    DateTime Timestamp,
    decimal SlippageApplied,
    decimal SpreadApplied);
