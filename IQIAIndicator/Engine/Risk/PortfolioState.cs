using System.Collections.Generic;

namespace IQIAIndicator.Engine.Risk;

/// <summary>
/// Sprint 15.25 (Lot 10, Section 13). One currently-open position, as needed for a future multi-position
/// portfolio view. Not aggregated into any calculation by RiskEngine this sprint.
/// </summary>
public sealed record OpenPosition(
    string Symbol,
    TradeDirection Direction,
    int Quantity,
    decimal EntryPrice,
    decimal StopLoss,
    decimal RiskAmount);

/// <summary>
/// Sprint 15.25 (Lot 10, Section 13). Interfaces/models only, in preparation for multi-position support -
/// no portfolio manager is implemented this sprint. RiskEngine.Evaluate does NOT read OpenPositions,
/// OpenRisk, DailyRiskUsed or CurrentEquity from this record; those concepts are already covered by
/// AccountState (the single source of truth for the scalar risk-budget math today). This record exists
/// purely so a future multi-position aggregation step has somewhere to plug in without a breaking change
/// to RiskEngineRequest.
/// </summary>
public sealed record PortfolioState(
    IReadOnlyList<OpenPosition> OpenPositions,
    decimal OpenRisk,
    decimal DailyRiskUsed,
    decimal CurrentEquity);
