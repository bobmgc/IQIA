using IQIAIndicator.Core;
using IQIAIndicator.Engine.Regime.Core;

namespace IQIAIndicator.Backtest;

/// <summary>
/// Sprint 15.25 (Lot 14.1). Passive, read-only observation hook for one bar the
/// <see cref="BacktestEngine"/> has already built a context for and run through
/// <see cref="Engine.Regime.RegimeEngine"/>. Mirrors the discipline
/// <c>Core.Observability.IPipelineTraceCollector</c> already established elsewhere in this pipeline:
/// an observer never influences the engine, it only gets to look at what already happened.
///
/// This exists so tests (look-ahead, determinism, parity - Lot 14.1 brief §15/§17/§20) can inspect the
/// exact <see cref="MarketContext"/>/<see cref="EvidenceSet"/> the engine produced per bar without
/// re-implementing the bar loop. It is not a production extension point for trading logic - later lots
/// (Fusion/Decision/Signal/Entry/TradePlan/Risk/Execution, brief §11 point 7) will need their own,
/// larger seam once wired in; this one is scoped to exactly what this lot computes.
/// </summary>
public interface IBacktestBarObserver
{
    /// <summary>Called once per bar that passed <see cref="MarketContextValidator"/>, in chronological
    /// order, immediately after <see cref="Engine.Regime.RegimeEngine.Collect"/> produced
    /// <paramref name="evidence"/> for it. Never called for a rejected bar - see
    /// <see cref="BacktestFoundationResult.BarsRejected"/>.</summary>
    void OnBarProcessed(int index, bool isWarmup, MarketContext context, EvidenceSet evidence);

    /// <summary>Called once per bar that <see cref="MarketContextValidator"/> rejected, in chronological
    /// order. The bar never reaches <see cref="Engine.Regime.RegimeEngine"/> - mirrors
    /// <c>IQIAIndicator.OnCalculate</c>'s own early-return on an invalid context.</summary>
    void OnBarRejected(int index, MarketContext context, IReadOnlyList<string> validationErrors);
}
