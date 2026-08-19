namespace IQIAIndicator.Engine.Risk;

/// <summary>
/// Sprint 15.25 (Lot 10). Everything RiskEngine.Evaluate needs for a single candidate trade. Deliberately
/// decoupled from EntryTriggerCandidate/TradePlanContext - the Risk Engine has no dependency on the
/// signal pipeline or on ATAS. StopLoss/TakeProfit are caller-provided (see IStopLossStrategy); the
/// engine never fabricates either. Portfolio is optional - a null value means "no open positions" and is
/// informational only (see PortfolioState).
/// </summary>
public sealed record RiskEngineRequest(
    TradeDirection Direction,
    decimal EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    InstrumentRiskSpecification Instrument,
    AccountState Account,
    RiskPolicy Policy,
    PortfolioState? Portfolio = null);
