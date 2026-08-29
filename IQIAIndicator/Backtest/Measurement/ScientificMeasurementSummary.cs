using System.Collections.Generic;

namespace IQIAIndicator.Backtest.Measurement;

/// <summary>One threshold's hit rate over a population of measured signals (brief §32/§34). A FRACTION
/// in [0,1] - never a percentage inside this type (brief §15's decimal convention, applied consistently
/// to aggregation too).</summary>
public sealed record MeasurementHitRateResult(double Threshold, double HitRate, int HitCount, int MeasuredCount);

/// <summary>
/// Sprint 15.25 (Lot 14.4, brief §32/§33/§34). Aggregate statistics over a population of
/// <see cref="MeasurementResult"/> restricted to <see cref="MeasurementStatus.Measured"/> - never a
/// NotMeasurable/InvalidEntryPrice/InsufficientFutureData/InvalidFutureData result silently averaged in
/// (their Return/Mfe/Mae are null by construction, brief §33: "valeurs invalides... valeurs partielles"
/// must be excluded from the median pool, not zero-filled).
///
/// MEDIAN ONLY (brief §32: "Ne pas produire de moyenne si elle n'est pas demandée") - no mean, no
/// standard deviation, no Sharpe, no win rate expressed as a percentage of account P&amp;L (there is no
/// P&amp;L in this lot at all - brief §35).
/// </summary>
/// <param name="Count">Number of Measured signals this summary is built from.</param>
/// <param name="MedianReturn">Median of <see cref="MeasurementResult.Return"/> over Measured signals, or
/// null when <see cref="Count"/> is 0. See <see cref="ScientificMeasurementSummaryBuilder.Median"/> for
/// the exact odd/even convention.</param>
/// <param name="MedianMfe">Median of <see cref="MeasurementResult.Mfe"/>.</param>
/// <param name="MedianMae">Median of <see cref="MeasurementResult.Mae"/>.</param>
/// <param name="HitRates">One entry per threshold present on the summarized measurements, in the same
/// order as the originating <see cref="MeasurementConfiguration.HitThresholds"/>.</param>
public sealed record ScientificMeasurementSummary(
    int Count,
    double? MedianReturn,
    double? MedianMfe,
    double? MedianMae,
    IReadOnlyList<MeasurementHitRateResult> HitRates);

/// <summary>Brief §16/§34: BUY/SELL statistics must never be lost inside an aggregated ALL total - all
/// three are always produced side by side.</summary>
public sealed record ScientificMeasurementSummaryByDirection(
    ScientificMeasurementSummary All,
    ScientificMeasurementSummary Buy,
    ScientificMeasurementSummary Sell);
