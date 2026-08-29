using System;

namespace IQIAIndicator.Backtest;

/// <summary>
/// Sprint 15.25 (Lot 14.1, brief §11). Minimal, deliberately narrow output of <see cref="BacktestEngine"/>
/// for this lot only - no trade, no PnL, no equity curve exists yet (those are later lots, see the
/// companion report §19/§28). This is a foundation-level receipt: how many bars this run actually
/// processed, how the scenario is identified, and a fingerprint proving the run is reproducible.
/// </summary>
public sealed record BacktestFoundationResult(
    /// <summary>Bars that passed MarketContextValidator and were fed to RegimeEngine, in chronological order.</summary>
    int BarsProcessed,
    /// <summary>Bars that failed MarketContextValidator and were skipped before reaching RegimeEngine -
    /// mirrors IQIAIndicator.OnCalculate's own early return on an invalid context.</summary>
    int BarsRejected,
    /// <summary>Of the processed bars, how many fell within the configured warmup length (index &lt;
    /// warmupBars). Counted, never silently treated as a valid signal - see the Lot 14.1 brief §13.
    /// No downstream capability (Decision/Entry/TradePlan) exists yet in this lot to actually gate on
    /// this; it exists so the boundary is visible and testable from day one.</summary>
    int WarmupBars,
    /// <summary>Earliest timestamp in the scenario's HistoricalSeries (not limited to processed bars -
    /// this describes the series' own coverage, independent of any rejection).</summary>
    DateTime FirstTimestamp,
    /// <summary>Latest timestamp in the scenario's HistoricalSeries.</summary>
    DateTime LastTimestamp,
    /// <summary>Deterministic identifier of the scenario that produced this result - a SHA-256 digest of
    /// the scenario's own identity fields (series identity + window + capital + instrument + policy),
    /// never a random Guid and never wall-clock derived (Lot 14.1 brief §17: "Aucun DateTime.UtcNow ne
    /// doit être utilisé pour définir le résultat"). Two BacktestScenario instances built from identical
    /// inputs always produce the same ScenarioId.</summary>
    string ScenarioId,
    /// <summary>Deterministic fingerprint of everything this run actually computed per bar (timestamp,
    /// OHLC, warmup flag, and every RegimeEngine evidence validity/statistic) - a SHA-256 digest, built
    /// without any wall-clock or random input. Identical scenario + identical code ⇒ identical hash; this
    /// is the quantity BacktestDeterminismTests compares across repeated runs.</summary>
    string DeterministicHash);
