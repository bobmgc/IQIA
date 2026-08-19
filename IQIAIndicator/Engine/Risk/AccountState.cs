using System;

namespace IQIAIndicator.Engine.Risk;

/// <summary>
/// Sprint 15.25 (Lot 10, Section 3). Explicit financial state of the trading account. Every field must
/// be supplied by the caller (configuration/context) - the Risk Engine never invents a default capital,
/// equity or drawdown value. CurrentDrawdown/CurrentDrawdownPercent are computed from PeakEquity/
/// CurrentEquity only (pure function of this record); DailyLossRemaining and AvailableRiskBudget also
/// depend on RiskPolicy, so they are produced by RiskEngine.Evaluate as part of RiskAssessment rather
/// than stored here.
/// </summary>
public sealed record AccountState(
    decimal InitialCapital,
    decimal CurrentEquity,
    decimal? CurrentBalance,
    decimal PeakEquity,
    decimal DailyStartingEquity,
    decimal DailyPnL,
    decimal RiskUsedToday,
    decimal OpenRisk)
{
    /// <summary>Absolute distance from PeakEquity down to CurrentEquity, floored at zero (never negative).</summary>
    public decimal CurrentDrawdown => Math.Max(0m, PeakEquity - CurrentEquity);

    /// <summary>CurrentDrawdown expressed as a fraction of PeakEquity (0.01 = 1%). Null if PeakEquity is not positive.</summary>
    public decimal? CurrentDrawdownPercent => PeakEquity > 0m ? CurrentDrawdown / PeakEquity : null;
}
