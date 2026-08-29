using System;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §3/§5/§6). The <c>Signal Price -&gt; Execution Model -&gt; Executed Price</c>
/// step of the brief's own architecture diagram (§3). STATELESS BY DESIGN (brief §11/§33): static class, no
/// fields, no randomness, no wall-clock, no global state - the same (price, side, quantity, timestamp,
/// slippage, spread) input always produces the exact same <see cref="ExecutedPrice"/>.
///
/// Composition rule: the spread pushes the theoretical (mid) price out to its own bid/ask half-width
/// first, then slippage pushes it further in the SAME adverse direction (brief §5/§6 describe each in
/// isolation; this class is where they compose) - <c>Price = TheoreticalPrice +/- (Spread/2 + Slippage)</c>,
/// sign chosen so the fill is ALWAYS adverse to the order's own side (brief §5: "Le comportement doit être
/// strictement déterministe" - never favourable, never zero unless configured zero).
/// </summary>
public static class ExecutionPriceModel
{
    /// <summary>
    /// Brief §5 "Direction du slippage" / §6 "BUY exécution côté ASK, SELL exécution côté BID", applied to
    /// this one order's <paramref name="side"/> - a Buy order always pays UP (ask + slippage), a Sell order
    /// always receives DOWN (bid - slippage), regardless of whether that order is opening or closing a
    /// position (see <see cref="EntryFillSide"/>/<see cref="ExitFillSide"/> for how a position's direction
    /// maps to each leg's side).
    /// </summary>
    public static ExecutedPrice Apply(
        decimal theoreticalPrice,
        OrderSide side,
        int quantity,
        DateTime timestamp,
        SlippageConfiguration slippage,
        SpreadConfiguration spread)
    {
        ArgumentNullException.ThrowIfNull(slippage);
        ArgumentNullException.ThrowIfNull(spread);

        decimal halfSpread = spread.PriceUnits / 2m;
        decimal sign = side == OrderSide.Buy ? 1m : -1m;
        decimal executedPrice = theoreticalPrice + sign * (halfSpread + slippage.PriceUnits);

        return new ExecutedPrice(theoreticalPrice, executedPrice, side, quantity, timestamp, slippage.PriceUnits, halfSpread);
    }

    /// <summary>A long (BUY_CANDIDATE) opens with a Buy order; a short (SELL_CANDIDATE) opens with a Sell
    /// order. Only ever called for a Closed <see cref="Execution.SimulatedPosition"/> (brief §5/§19 of Lot
    /// 14.5: NO_ACTION/WATCH never reach Closed), so the two directional values are exhaustive here.</summary>
    public static OrderSide EntryFillSide(DirectionCandidate direction) => direction switch
    {
        DirectionCandidate.BUY_CANDIDATE => OrderSide.Buy,
        DirectionCandidate.SELL_CANDIDATE => OrderSide.Sell,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Only BUY_CANDIDATE/SELL_CANDIDATE positions ever reach Closed and have a fill side.")
    };

    /// <summary>Closing a long is a Sell order; closing a short is a Buy order - the mirror image of
    /// <see cref="EntryFillSide"/>.</summary>
    public static OrderSide ExitFillSide(DirectionCandidate direction) => direction switch
    {
        DirectionCandidate.BUY_CANDIDATE => OrderSide.Sell,
        DirectionCandidate.SELL_CANDIDATE => OrderSide.Buy,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Only BUY_CANDIDATE/SELL_CANDIDATE positions ever reach Closed and have a fill side.")
    };
}
