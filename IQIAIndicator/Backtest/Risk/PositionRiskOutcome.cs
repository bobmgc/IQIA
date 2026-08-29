using System;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Backtest.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8). One <see cref="SimulatedPosition"/> (Lot 14.5), enriched with its risk
/// evaluation (brief §9), the resulting cost/PnL at the RISK-DETERMINED quantity (never the raw
/// <c>PnLConfiguration.Quantity</c> - brief §15: "Les coûts doivent être calculés sur la quantité
/// réellement autorisée"), and the running equity snapshot around it (brief §13).
///
/// <see cref="EquityBefore"/>/<see cref="EquityAfter"/>/<see cref="Drawdown"/> are null for any position
/// that never entered the sequential risk/equity walk (brief §5's own "never a fabricated zero" discipline,
/// applied here) - either because it never reached <see cref="PositionStatus.Closed"/>, or (structurally
/// unreachable given <see cref="BacktestRiskResultBuilder"/>'s own construction, but never assumed) it was
/// not evaluated for some other reason. <see cref="CostResult"/>/<see cref="NetPnL"/> are likewise null
/// whenever <see cref="RiskEvaluation"/>.AllowedQuantity is 0 - brief §17 "Rejection": "aucune exécution,
/// aucun coût, aucun PnL, aucune modification d'Equity".
/// </summary>
public sealed record PositionRiskOutcome(
    int PositionId,
    PositionStatus Status,
    DirectionCandidate Direction,
    DateTime EntryTimestamp,
    DateTime? ExitTimestamp,
    RiskEvaluationResult RiskEvaluation,
    PositionCostResult? CostResult,
    decimal? NetPnL,
    decimal? EquityBefore,
    decimal? EquityAfter,
    decimal? Drawdown);
