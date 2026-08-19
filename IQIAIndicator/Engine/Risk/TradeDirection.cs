namespace IQIAIndicator.Engine.Risk;

/// <summary>
/// Sprint 15.25 (Lot 10). The Risk Engine's own directional type - deliberately independent from
/// EntryTrigger.DirectionCandidate (which also carries WATCH/NO_ACTION, meaningless for a risk
/// calculation). Keeps the Risk Engine free of any dependency on the signal pipeline; mapping from
/// DirectionCandidate.BUY_CANDIDATE/SELL_CANDIDATE happens at the (future) integration point, not here.
/// </summary>
public enum TradeDirection
{
    Buy,
    Sell
}
