using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Engine.Risk;

/// <summary>
/// Sprint 15.25 (Lot 11). The one file in this integration that is allowed to depend on the signal
/// pipeline's TradePlan type - RiskEngine.cs itself (Lot 10) stays entirely pipeline-independent.
/// Maps an already-built TradePlan onto a RiskEngineRequest without ever reconstructing or recomputing
/// it: Direction/EntryPrice/StopLoss/TakeProfit are taken exactly as TradePlanBuilder produced them
/// (Lot 11, Section 7). Returns null when TradePlan has no assessable directional candidate (Direction
/// is not BUY_CANDIDATE/SELL_CANDIDATE, or EntryPrice could not be sourced) - the same categorical-gap
/// philosophy TradePlanBuilder itself already applies for NO_TRADE. There is nothing for the Risk
/// Engine to evaluate in that case; it is never called with an invented entry.
/// </summary>
public static class RiskEngineRequestFactory
{
    public static RiskEngineRequest? FromTradePlan(
        global::IQIAIndicator.Engine.TradePlan.TradePlan tradePlan,
        AccountState account,
        RiskPolicy policy,
        InstrumentRiskSpecification instrument,
        PortfolioState? portfolio = null)
    {
        // Audit 2026-08-29: a plan the builder already rejected on a policy gate (PLAN_REJECTED, e.g.
        // RiskRewardRatio below TradeRiskParameters.MinRiskReward) is not re-evaluated here - the
        // rejection and its reason are already carried by the TradePlan itself; handing it to the Risk
        // Engine would only surface a second, redundant REJECTED for the same decision.
        if (tradePlan.Status == global::IQIAIndicator.Engine.TradePlan.TradePlanStatus.PLAN_REJECTED)
            return null;

        TradeDirection? direction = tradePlan.Direction switch
        {
            DirectionCandidate.BUY_CANDIDATE => TradeDirection.Buy,
            DirectionCandidate.SELL_CANDIDATE => TradeDirection.Sell,
            _ => null
        };

        if (direction is null || tradePlan.EntryPrice is not decimal entryPrice)
            return null;

        return new RiskEngineRequest(
            direction.Value,
            entryPrice,
            tradePlan.StopLoss,
            tradePlan.TakeProfit,
            instrument,
            account,
            policy,
            portfolio);
    }
}
