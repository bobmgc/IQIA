namespace IQIAIndicator.Engine.Risk;

/// <summary>
/// Sprint 15.25 (Lot 11). Bridges ATAS's UI parameter model (non-nullable primitives - every existing
/// indicator setting in IQIAIndicator.cs, e.g. TickValue/PointValue, is a plain decimal/int/bool) with
/// RiskPolicy's nullable "opt-in constraint" model (Lot 10: null = not constrained by this rule).
///
/// Two conventions applied here, both plumbing only - neither decides what any limit SHOULD be:
/// 1. A limit &lt;= 0 means "not configured yet" -&gt; RiskPolicy null (unconstrained).
/// 2. The four *Percent inputs are WHOLE percents as a human would type them in ATAS's property grid
///    (2 means 2%), converted to the fraction RiskPolicy actually stores (0.02 - see
///    AccountState.CurrentDrawdownPercent for the same fraction convention). Passing the raw fraction
///    straight through would be a silent, dangerous footgun: a user typing "2" expecting "2%" would
///    otherwise configure a 200% risk-per-trade limit instead of a no-op safety cap.
/// </summary>
public static class RiskPolicyFactory
{
    public static RiskPolicy FromRawInputs(
        decimal maxRiskPerTradeWholePercent,
        decimal maxRiskPerTradeAmount,
        decimal maxDailyLossWholePercent,
        decimal maxDailyLossAmount,
        decimal maxDrawdownWholePercent,
        decimal maxDrawdownAmount,
        decimal maxOpenRiskWholePercent,
        decimal maxOpenRiskAmount,
        double minRiskReward,
        int maxPositionSize) =>
        new(
            PositiveFractionOrNull(maxRiskPerTradeWholePercent),
            PositiveOrNull(maxRiskPerTradeAmount),
            PositiveFractionOrNull(maxDailyLossWholePercent),
            PositiveOrNull(maxDailyLossAmount),
            PositiveFractionOrNull(maxDrawdownWholePercent),
            PositiveOrNull(maxDrawdownAmount),
            PositiveFractionOrNull(maxOpenRiskWholePercent),
            PositiveOrNull(maxOpenRiskAmount),
            PositiveOrNull(minRiskReward),
            PositiveOrNull(maxPositionSize));

    private static decimal? PositiveOrNull(decimal value) => value > 0m ? value : null;

    private static decimal? PositiveFractionOrNull(decimal wholePercent) => wholePercent > 0m ? wholePercent / 100m : null;

    private static double? PositiveOrNull(double value) => value > 0 ? value : null;

    private static int? PositiveOrNull(int value) => value > 0 ? value : null;
}
