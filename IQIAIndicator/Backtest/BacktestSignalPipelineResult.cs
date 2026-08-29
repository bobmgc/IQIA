namespace IQIAIndicator.Backtest;

/// <summary>
/// Sprint 15.25 (Lot 14.3, brief §16/§17). Aggregate result of one full-pipeline backtest run. Every
/// counter here is a TECHNICAL count of pipeline outcomes (brief §17: "INTERDIT de calculer profit, win
/// rate, Sharpe, drawdown, expectancy") - never a financial metric, never derived from a simulated fill
/// or a P&amp;L.
/// </summary>
/// <param name="BarsProcessed">Bars that passed <see cref="Core.MarketContextValidator"/> and were run
/// through the pipeline (Warmup + Ready + Exception bars). Mirrors
/// <see cref="BacktestFoundationResult.BarsProcessed"/>.</param>
/// <param name="BarsRejected">Bars <see cref="Core.MarketContextValidator"/> rejected before the
/// pipeline ever ran for them. Mirrors <see cref="BacktestFoundationResult.BarsRejected"/>.</param>
/// <param name="WarmupBars">Processed bars below the caller-supplied warmup threshold.</param>
/// <param name="ReadyBars">Processed bars at or past the warmup threshold (BarsProcessed - WarmupBars).</param>
/// <param name="RegimeDetectedCount">Bars where <see cref="Engine.Decision.Core.DecisionResult.Winner"/>
/// resolved to a concrete <see cref="Engine.Decision.States.MarketState"/> (not
/// <see cref="Engine.Decision.States.MarketState.Unknown"/>) - i.e. the regime evidence this bar
/// produced was enough for the Decision layer to arbitrate a market state.</param>
/// <param name="DecisionCount">Bars where at least one <c>IDecisionRule</c> triggered
/// (<c>DecisionResult.Candidates.Length &gt; 0</c>). In this pipeline this is, by construction
/// (<c>DecisionEngine.Evaluate</c>), the same set of bars as <see cref="RegimeDetectedCount"/> - Winner
/// only leaves its Unknown default when at least one candidate was arbitrated. Reported separately
/// because they measure two different questions (did a rule fire vs. did arbitration name a regime),
/// not because they are expected to diverge on real data.</param>
/// <param name="SignalCount">Bars where the Signal stage actually ran a non-empty scientific model
/// battery (<c>scientificResults.Count &gt; 0</c>) - today only possible when Methodology resolved to
/// MeanReversionMethodology (see <c>ScientificModelRegistry.Resolve</c>, unmodified).</param>
/// <param name="EntryCandidateCount">Bars where the Entry stage recognised some opportunity
/// (<c>OpportunityStatus != NOT_QUALIFIED</c>): WATCHLIST, QUALIFIED or HIGH_PRIORITY.</param>
/// <param name="BuyCount">Bars where EntryTrigger direction resolved to BUY_CANDIDATE.</param>
/// <param name="SellCount">Bars where EntryTrigger direction resolved to SELL_CANDIDATE.</param>
/// <param name="NoActionCount">Bars where EntryTrigger direction resolved to NO_ACTION.</param>
/// <param name="WatchCount">Bars where EntryTrigger direction resolved to WATCH. Not requested by name
/// in the brief's minimum list (§17) but the same kind of technical, non-financial count as the other
/// three Direction values - omitting it would silently under-report the Direction breakdown.</param>
/// <param name="TradePlanSignalOnlyCount">Bars where TradePlan.Status == SIGNAL_ONLY.</param>
/// <param name="TradePlanReadyCount">Bars where TradePlan.Status == PLAN_READY.</param>
/// <param name="TradePlanNoTradeCount">Bars where TradePlan.Status == NO_TRADE.</param>
/// <param name="TradePlanBlockedCount">Bars where TradePlan.Status == PLAN_BLOCKED.</param>
/// <param name="ExceptionCount">Bars whose <see cref="BacktestSignalResult.Status"/> is
/// <see cref="BacktestSignalStatus.Exception"/>.</param>
public sealed record BacktestSignalPipelineResult(
    int BarsProcessed,
    int BarsRejected,
    int WarmupBars,
    int ReadyBars,
    int RegimeDetectedCount,
    int DecisionCount,
    int SignalCount,
    int EntryCandidateCount,
    int BuyCount,
    int SellCount,
    int NoActionCount,
    int WatchCount,
    int TradePlanSignalOnlyCount,
    int TradePlanReadyCount,
    int TradePlanNoTradeCount,
    int TradePlanBlockedCount,
    int ExceptionCount,
    DateTime FirstTimestamp,
    DateTime LastTimestamp,
    string ScenarioId,
    string DeterministicHash,
    IReadOnlyList<BacktestSignalResult> Bars);
