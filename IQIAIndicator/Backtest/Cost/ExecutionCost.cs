namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §4 "ExecutionCost" / §7 "totalCost = commission + fees + spreadCost +
/// slippageCost"). One trade's (round-trip: entry + exit) full cost breakdown, in dollars (already
/// multiplied by PriceUnitValue and Quantity - see <see cref="PositionCostCalculator"/>). Every component
/// is independently inspectable (brief §12's "Combined costs" tests check each one, not just the total) and
/// <see cref="TotalCost"/> is always exactly their sum - never a fifth, independently-computed number
/// (brief §7: "Éviter toute duplication ou double comptabilisation").
/// </summary>
public sealed record ExecutionCost(
    decimal Commission,
    decimal Fees,
    decimal SpreadCost,
    decimal SlippageCost,
    decimal TotalCost);
