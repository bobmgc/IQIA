namespace IQIAIndicator.Engine.Risk;

/// <summary>
/// Sprint 15.25 (Lot 10, Section 11). Structured rejection reasons - never a free-form string. Naming
/// follows the SCREAMING_SNAKE_CASE convention already used by TradePlan's neighbouring pipeline enums
/// (TradePlanStatus, EntryTriggerReason). A RiskAssessment may carry more than one simultaneously (e.g.
/// invalid capital AND max drawdown reached at once) - see RiskEngine.Evaluate.
/// </summary>
public enum RiskRejectionReason
{
    INVALID_CAPITAL,
    INVALID_EQUITY,
    RISK_BUDGET_EXCEEDED,
    DAILY_LOSS_LIMIT,
    MAX_DRAWDOWN_REACHED,
    OPEN_RISK_LIMIT,
    INVALID_ENTRY,
    INVALID_STOP_LOSS,
    INVALID_TAKE_PROFIT,
    INVALID_RISK_REWARD,
    POSITION_SIZE_INVALID,
    INSTRUMENT_SPEC_INVALID,
    QUANTITY_LIMIT,
    UNKNOWN_ERROR
}
