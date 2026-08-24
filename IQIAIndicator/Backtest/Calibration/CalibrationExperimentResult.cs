using System.Collections.Generic;
using System.Linq;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §22/§23/§26). One (Window, Direction) slice of one
/// <see cref="CalibrationExperiment"/>'s outcome. Every experiment produces NINE of these (TRAIN/
/// VALIDATION/OOS x ALL/BUY/SELL, brief §23) - never collapsed into a single number.
///
/// Every metric is REUSED from the Lot 14.3-14.8 pipeline's own already-computed per-bar/per-position
/// results (brief §22: "Ne pas recalculer les statistiques si elles existent déjà"); this type only
/// filters those results to one window/direction and aggregates (count/sum/median/ratio) - it never
/// reimplements a Return/MFE/MAE/PnL/Drawdown formula.
///
/// <see cref="GrossPnL"/>/<see cref="FinalEquity"/>/<see cref="MaximumDrawdown"/> are sourced from
/// <c>Risk.BacktestRiskResult</c>'s per-position <c>NetPnL</c>/running equity (Lot 14.8) - the terminal,
/// fully-computed PnL the pipeline produces end-to-end (post risk-based sizing AND cost/slippage). No
/// separate pre-cost "gross" figure survives Lot 14.8's per-position quantity substitution (documented
/// fact, not an oversight - see this lot's report §Limitations); naming it GrossPnL here matches brief
/// §22's own field name while this doc comment states exactly what it contains.
///
/// NO SCORE: this type deliberately has no combined/weighted "CalibrationScore" field (brief §26 -
/// forbidden in this lot). Comparing two results is left entirely to the caller.
/// </summary>
public sealed record CalibrationExperimentResult
{
    public required string ExperimentId { get; init; }

    public required string ConfigurationFingerprint { get; init; }

    public required string DatasetFingerprint { get; init; }

    public required CalibrationWindow Window { get; init; }

    public required CalibrationDirectionFilter Direction { get; init; }

    /// <summary>Brief §22 "status" - <see cref="CalibrationExperimentResultStatus.NoData"/> when zero
    /// signals fell in this (Window, Direction) slice, never a silently zeroed
    /// <see cref="CalibrationExperimentResultStatus.Succeeded"/>.</summary>
    public required CalibrationExperimentResultStatus Status { get; init; }

    public required int SignalCount { get; init; }

    /// <summary>Closed theoretical positions in this slice whose risk evaluation ALLOWED a non-zero
    /// quantity - see the class doc comment for why (rejected positions carry no NetPnL to aggregate).</summary>
    public required int PositionCount { get; init; }

    public decimal? GrossPnL { get; init; }

    public decimal? FinalEquity { get; init; }

    public decimal? MaximumDrawdown { get; init; }

    public double? WinRate { get; init; }

    public double? MedianReturn { get; init; }

    public double? MedianMfe { get; init; }

    public double? MedianMae { get; init; }

    /// <summary>One entry per <c>MeasurementConfiguration.HitThresholds</c> value (brief §25 "HitRate(s)") -
    /// empty when <see cref="Status"/> is <see cref="CalibrationExperimentResultStatus.NoData"/>.</summary>
    public required IReadOnlyDictionary<double, double> HitRates { get; init; }

    public required string ResultFingerprint { get; init; }

    internal static CalibrationExperimentResult Create(
        string experimentId,
        string configurationFingerprint,
        string datasetFingerprint,
        CalibrationWindow window,
        CalibrationDirectionFilter direction,
        CalibrationExperimentResultStatus status,
        int signalCount,
        int positionCount,
        decimal? grossPnL,
        decimal? finalEquity,
        decimal? maximumDrawdown,
        double? winRate,
        double? medianReturn,
        double? medianMfe,
        double? medianMae,
        IReadOnlyDictionary<double, double> hitRates)
    {
        string resultFingerprint = CalibrationFingerprint.ComputeResultFingerprint(
            experimentId, window, direction, status, signalCount, positionCount, grossPnL, finalEquity,
            maximumDrawdown, winRate, medianReturn, medianMfe, medianMae,
            hitRates.Select(kv => (Threshold: kv.Key, Rate: kv.Value)).ToArray());

        return new CalibrationExperimentResult
        {
            ExperimentId = experimentId,
            ConfigurationFingerprint = configurationFingerprint,
            DatasetFingerprint = datasetFingerprint,
            Window = window,
            Direction = direction,
            Status = status,
            SignalCount = signalCount,
            PositionCount = positionCount,
            GrossPnL = grossPnL,
            FinalEquity = finalEquity,
            MaximumDrawdown = maximumDrawdown,
            WinRate = winRate,
            MedianReturn = medianReturn,
            MedianMfe = medianMfe,
            MedianMae = medianMae,
            HitRates = hitRates,
            ResultFingerprint = resultFingerprint
        };
    }
}
