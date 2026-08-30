using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.Risk;

/// <summary>
/// Sprint 15.25 (Lot 15.3). First real implementation of <see cref="IStopLossStrategy"/> (the extension
/// point declared at Lot 10, never implemented beyond the no-op <see cref="ProvidedStopLossStrategy"/>
/// pass-through - see the Lot 15.0/15.2 audits). Scientific input: <c>VolatilityModel</c>'s
/// <c>CurrentVolatility</c> metric (<see cref="ScientificMetricKeys.CurrentVolatility"/>) - a causally
/// audited (Lot B2), already-tested, already-computed-for-every-MeanReverting-bar standard deviation of
/// trailing 20-bar returns, in price units. Chosen over inventing a new ATR calculation (brief §4: "ne
/// pas supposer que ATR est automatiquement le bon choix... comparer scientifiquement les candidats
/// existants") because it is the ONLY volatility-shaped evidence already produced, already causal, and
/// already reachable without touching a protected file - <c>Engine.Regime.Evidence.VolatilityEvidence</c>
/// (ACF of |returns|, a clustering test, not a distance measure) and the raw <c>Hurst</c> evidence are
/// both unconsumed elsewhere in the pipeline and neither expresses a price-scale distance.
///
/// NON CALIBRATED (brief §22, §"RÈGLE ABSOLUE"): <see cref="DefaultVolatilityMultiplier"/> = 2.0 is a
/// conventional "two standard deviations" distance factor, chosen for being a standard, explainable
/// convention - NEVER grid-searched, NEVER selected against this project's PnL/win-rate/Sharpe/
/// expectancy. A future, explicitly separate calibration lot may revise it via a documented walk-forward/
/// OOS methodology; this lot must not, and does not.
///
/// SCOPE: only ever produces a stop for a BUY_CANDIDATE/SELL_CANDIDATE direction - never invents one for
/// NO_ACTION/WATCH (brief §20), and never fabricates a value when volatility/tick-size/entry-price are
/// missing or degenerate (brief §9) - <see cref="TryResolveStopPrice"/> returns null in every such case,
/// exactly the "categorical gap, never invented" discipline <c>TradePlanBuilder</c> already applies
/// throughout (Lot 15.8).
/// </summary>
public sealed class VolatilityStopLossModel : IStopLossStrategy
{
    /// <summary>NON CALIBRATED (see class doc comment) - a standard "2 standard deviations" convention,
    /// never tuned against this project's data.</summary>
    public const double DefaultVolatilityMultiplier = 2.0;

    private readonly decimal _currentVolatility;
    private readonly decimal _tickSize;
    private readonly double _multiplier;

    /// <param name="currentVolatility">Price-scale volatility distance (e.g. VolatilityModel's
    /// CurrentVolatility). Must be strictly positive - callers with a possibly-invalid/unavailable value
    /// should use <see cref="TryResolveStopPrice"/> instead of constructing this directly.</param>
    /// <param name="tickSize">Instrument tick size. Must be strictly positive.</param>
    /// <param name="multiplier">Distance factor. Must be finite and strictly positive. Defaults to the
    /// non-calibrated <see cref="DefaultVolatilityMultiplier"/>.</param>
    public VolatilityStopLossModel(decimal currentVolatility, decimal tickSize, double multiplier = DefaultVolatilityMultiplier)
    {
        if (currentVolatility <= 0m)
            throw new ArgumentOutOfRangeException(nameof(currentVolatility), currentVolatility, "CurrentVolatility must be strictly positive.");
        if (tickSize <= 0m)
            throw new ArgumentOutOfRangeException(nameof(tickSize), tickSize, "TickSize must be strictly positive.");
        if (!double.IsFinite(multiplier) || multiplier <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(multiplier), multiplier, "Multiplier must be finite and strictly positive.");

        _currentVolatility = currentVolatility;
        _tickSize = tickSize;
        _multiplier = multiplier;
    }

    /// <summary>
    /// Resolves a tick-aligned stop price, or null if the result would be degenerate after rounding.
    ///
    /// TICK ROUNDING (brief §10): rounds the raw volatility-implied stop TOWARD Entry for both
    /// directions (BUY: Math.Ceiling: SELL: Math.Floor) - never away from it. This is the conservative
    /// direction for the risk-sizing chain downstream (TradePlanBuilder computes RiskPerUnit/PositionSize
    /// from whichever StopLoss value this method returns): rounding toward Entry can only SHRINK the
    /// realized StopDistance relative to the raw (pre-rounding) value, so the risk actually taken can
    /// never exceed what a caller who only inspects the final, rounded StopLoss would compute - never
    /// the opposite (rounding away from Entry would silently understate risk by widening the distance
    /// after sizing was already anchored to the narrower, pre-rounding one). Both directions are checked
    /// explicitly below to guarantee the tick-aligned stop never crosses to the wrong side of Entry.
    /// </summary>
    public decimal? Resolve(TradeDirection direction, decimal entryPrice)
    {
        if (entryPrice <= 0m)
            return null;

        decimal rawDistance = _currentVolatility * (decimal)_multiplier;
        decimal rawStop = direction == TradeDirection.Buy ? entryPrice - rawDistance : entryPrice + rawDistance;

        decimal roundedStop = direction == TradeDirection.Buy
            ? Math.Ceiling(rawStop / _tickSize) * _tickSize
            : Math.Floor(rawStop / _tickSize) * _tickSize;

        decimal finalDistance = direction == TradeDirection.Buy
            ? entryPrice - roundedStop
            : roundedStop - entryPrice;

        // Never fabricate a same-side or zero-distance stop - a degenerate result after rounding is
        // reported as "no stop available", exactly like every other invalid-input case, never silently
        // widened/narrowed to force a positive distance (brief §8/§9).
        return finalDistance > 0m ? roundedStop : null;
    }

    /// <summary>
    /// Safe entry point (brief §1: reuse what already exists - <c>EntryTriggerCandidate</c> already
    /// carries everything needed via <c>EntryCandidate.Assessment.ScientificAssessment.ScientificResults</c>,
    /// the same list <c>SignalEngine</c> already threads through unmodified). Never throws; returns null
    /// for every invalid/unavailable/non-directional case rather than fabricating a value:
    /// - Direction is not BUY_CANDIDATE/SELL_CANDIDATE (brief §20: never invents a direction).
    /// - CurrentPrice (used as EntryPrice, the SAME field <c>TradePlanBuilder</c> itself reads) is not
    ///   strictly positive.
    /// - <c>VolatilityModel</c>'s result is absent, unsuccessful, or its <c>CurrentVolatility</c> metric
    ///   is missing, non-finite, non-positive, or outside <see cref="decimal"/>'s representable range.
    /// - The resulting tick-aligned stop would be degenerate (see <see cref="Resolve"/>).
    /// Reusable identically from a live (ATAS) or backtest caller - depends only on Engine types, never on
    /// Backtest.* or ATAS.
    /// </summary>
    public static decimal? TryResolveStopPrice(
        EntryTriggerCandidate entryTriggerCandidate,
        decimal tickSize,
        double multiplier = DefaultVolatilityMultiplier)
    {
        ArgumentNullException.ThrowIfNull(entryTriggerCandidate);

        TradeDirection? direction = entryTriggerCandidate.Assessment.Direction switch
        {
            DirectionCandidate.BUY_CANDIDATE => TradeDirection.Buy,
            DirectionCandidate.SELL_CANDIDATE => TradeDirection.Sell,
            _ => null
        };

        if (direction is null || entryTriggerCandidate.CurrentPrice <= 0m || tickSize <= 0m)
            return null;

        if (!TryGetCurrentVolatility(entryTriggerCandidate, out decimal currentVolatility))
            return null;

        VolatilityStopLossModel model;
        try
        {
            model = new VolatilityStopLossModel(currentVolatility, tickSize, multiplier);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Defensive only - TryGetCurrentVolatility/the tickSize check above already guarantee valid
            // constructor inputs; this catch exists so an invalid multiplier supplied by a future caller
            // fails safe (null) rather than throwing out of what this method promises is a safe entry point.
            return null;
        }

        return model.Resolve(direction.Value, entryTriggerCandidate.CurrentPrice);
    }

    private static bool TryGetCurrentVolatility(EntryTriggerCandidate entryTriggerCandidate, out decimal currentVolatility)
    {
        currentVolatility = 0m;

        IReadOnlyList<ScientificModelResult>? results =
            entryTriggerCandidate.EntryCandidate?.Assessment?.ScientificAssessment?.ScientificResults;
        if (results is null)
            return false;

        // VolatilityModel is the mean-reversion source; audit 2026-08-30 (P0-2) added
        // TimeSeriesMomentumModel as the trend-following source (it exposes the same
        // ScientificMetricKeys.CurrentVolatility, in price units) - only one of the two ever runs on a
        // given bar (they are hard-gated to disjoint regimes), so first-match is unambiguous.
        ScientificModelResult? volatilityResult = null;
        foreach (ScientificModelResult result in results)
        {
            if (string.Equals(result.ModelName, "VolatilityModel", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.ModelName, "TimeSeriesMomentumModel", StringComparison.OrdinalIgnoreCase))
            {
                volatilityResult = result;
                break;
            }
        }

        if (volatilityResult is not { Success: true, Metrics: not null } validResult)
            return false;

        if (!validResult.Metrics.TryGetValue(ScientificMetricKeys.CurrentVolatility, out object? raw) ||
            raw is not double rawVolatility ||
            !double.IsFinite(rawVolatility) ||
            rawVolatility <= 0.0)
        {
            return false;
        }

        try
        {
            decimal converted = (decimal)rawVolatility;
            if (converted <= 0m)
                return false;

            currentVolatility = converted;
            return true;
        }
        catch (OverflowException)
        {
            // rawVolatility is finite but outside decimal's representable range - never fabricate.
            return false;
        }
    }
}
