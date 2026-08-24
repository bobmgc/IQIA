using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Risk;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §1/§47). Executes ONE <see cref="CalibrationExperiment"/> and slices its
/// output into TRAIN/VALIDATION/OOS x ALL/BUY/SELL <see cref="CalibrationExperimentResult"/>s.
///
/// EXECUTION STRATEGY (brief §15/§16/§17 - the look-ahead-safety argument this whole lot rests on):
/// <see cref="BacktestEngine.RunFullBacktestWithRisk"/> is called EXACTLY ONCE, over the WHOLE dataset
/// series, completely unmodified from Lot 14.8 - never once per window. Every stage of that pipeline
/// (Regime/Fusion/Decision/Signal/Entry/EntryTrigger/TradePlan/Measurement/Execution/Risk) already computes
/// bar i as a pure function of bars [0..i] only (proven by Lot 14.1's BacktestFoundationLookAheadTests and
/// Lot 14.3's BacktestSignalPipelineLookAheadTests, both still enforced, unmodified, on every commit).
/// Because of that pre-existing guarantee, slicing the ALREADY-COMPUTED per-bar/per-position results by
/// calendar timestamp AFTER the one full run is equivalent to (but far cheaper than) re-running the engine
/// three separate times with truncated series - TRAIN's slice can never be influenced by whether
/// VALIDATION/OOS bars exist in the same series, because the pipeline never looked at them to produce
/// TRAIN's own bars in the first place. <see cref="CalibrationLookAheadTests"/>/<see cref="CalibrationWindowIsolationTests"/>
/// verify this equivalence directly (brief §35/§36), rather than only asserting it here in prose.
///
/// The engine's own <c>warmupBars</c> parameter (index-based, Lot 14.1) is set to the count of dataset bars
/// strictly before TRAIN.Start - see <see cref="CalibrationWarmupContract"/>'s doc comment for why that
/// value does not otherwise affect this method's own window-by-timestamp bucketing.
/// </summary>
public static class CalibrationExperimentRunner
{
    public static CalibrationExperimentRunResult Run(CalibrationExperiment experiment)
    {
        ArgumentNullException.ThrowIfNull(experiment);

        HistoricalSeries series = experiment.Dataset.Series;
        var fullSpan = new BacktestWindow("CALIBRATION-FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1));
        BacktestScenario scenario = BacktestScenario.Create(
            series, fullSpan, experiment.Setup.InitialCapital, experiment.Setup.Instrument, experiment.Setup.Policy);

        int warmupBars = series.Bars.Count(b => b.Timestamp < experiment.Windows.Train.Start);

        // Sprint 15.25 (Lot 14.10, P0-3): the one place experiment.ParameterSet ever leaves the
        // fingerprint/identity layer and actually reaches the pipeline - see CalibrationParameterBinding's
        // own doc comment for exactly which parameter names have a real injection point today.
        PipelineParameterOverrides overrides = CalibrationParameterBinding.Resolve(experiment.ParameterSet);

        BacktestFullResultWithRisk fullResult = new BacktestEngine().RunFullBacktestWithRisk(
            scenario, warmupBars,
            experiment.Setup.MeasurementConfiguration, experiment.Setup.ExecutionConfiguration,
            experiment.Setup.PnLConfiguration, experiment.Setup.CostConfiguration, experiment.Setup.RiskConfiguration,
            overrides);

        var results = new List<CalibrationExperimentResult>(9);
        foreach (CalibrationWindow window in experiment.Windows.All())
        {
            foreach (CalibrationDirectionFilter direction in new[] { CalibrationDirectionFilter.All, CalibrationDirectionFilter.Buy, CalibrationDirectionFilter.Sell })
            {
                results.Add(BuildSlice(experiment, window, direction, fullResult.Measurements, fullResult.RiskResult.Outcomes));
            }
        }

        return new CalibrationExperimentRunResult(experiment.ExperimentId, experiment.ConfigurationFingerprint, results.AsReadOnly());
    }

    private static CalibrationExperimentResult BuildSlice(
        CalibrationExperiment experiment,
        CalibrationWindow window,
        CalibrationDirectionFilter direction,
        IReadOnlyList<MeasurementResult> measurements,
        IReadOnlyList<PositionRiskOutcome> outcomes)
    {
        List<MeasurementResult> directionalSignals = measurements
            .Where(m => window.Contains(m.SignalTimestamp) && IsDirectional(m.Direction) && Matches(m.Direction, direction))
            .ToList();

        int signalCount = directionalSignals.Count;

        List<MeasurementResult> measured = directionalSignals.Where(m => m.Status == MeasurementStatus.Measured).ToList();

        List<PositionRiskOutcome> windowOutcomes = outcomes
            .Where(o => o.Status == PositionStatus.Closed && window.Contains(o.EntryTimestamp) && Matches(o.Direction, direction))
            .OrderBy(o => o.ExitTimestamp)
            .ThenBy(o => o.PositionId)
            .ToList();

        List<PositionRiskOutcome> allowed = windowOutcomes.Where(o => o.RiskEvaluation.IsAllowed && o.NetPnL is not null).ToList();

        if (signalCount == 0 && windowOutcomes.Count == 0)
        {
            return CalibrationExperimentResult.Create(
                experiment.ExperimentId, experiment.ConfigurationFingerprint, experiment.Dataset.Fingerprint,
                window, direction, CalibrationExperimentResultStatus.NoData,
                signalCount: 0, positionCount: 0, grossPnL: null, finalEquity: null, maximumDrawdown: null,
                winRate: null, medianReturn: null, medianMfe: null, medianMae: null,
                hitRates: new Dictionary<double, double>());
        }

        // Same accumulation shape as Risk.BacktestRiskResultBuilder (brief §22: reuse, never reinvent) -
        // re-walked over this window/direction's own subset of already-computed NetPnL values, in the same
        // (ExitTimestamp, PositionId) chronological order.
        decimal cumulativeNetPnL = 0m;
        decimal peakCumulativeNetPnL = 0m;
        decimal maximumDrawdown = 0m;
        int wins = 0;

        foreach (PositionRiskOutcome outcome in allowed)
        {
            cumulativeNetPnL += outcome.NetPnL!.Value;
            if (cumulativeNetPnL > peakCumulativeNetPnL)
                peakCumulativeNetPnL = cumulativeNetPnL;

            decimal drawdown = cumulativeNetPnL - peakCumulativeNetPnL;
            if (drawdown < maximumDrawdown)
                maximumDrawdown = drawdown;

            if (outcome.NetPnL.Value > 0m)
                wins++;
        }

        int positionCount = allowed.Count;
        decimal? grossPnL = positionCount > 0 ? cumulativeNetPnL : null;
        decimal? finalEquity = positionCount > 0 ? experiment.Setup.InitialCapital + cumulativeNetPnL : null;
        decimal? maxDrawdown = positionCount > 0 ? maximumDrawdown : null;
        double? winRate = positionCount > 0 ? (double)wins / positionCount : null;

        double? medianReturn = Median(allowed.Select(o => TryFindReturn(o, measured)).Where(r => r is not null).Select(r => r!.Value));
        double? medianMfe = Median(measured.Where(m => m.Mfe is not null).Select(m => m.Mfe!.Value));
        double? medianMae = Median(measured.Where(m => m.Mae is not null).Select(m => m.Mae!.Value));

        var hitRates = new Dictionary<double, double>();
        if (measured.Count > 0)
        {
            foreach (double threshold in measured.SelectMany(m => m.HitResults.Select(h => h.Threshold)).Distinct())
            {
                List<bool> hitsForThreshold = measured
                    .SelectMany(m => m.HitResults.Where(h => h.Threshold == threshold))
                    .Select(h => h.Hit)
                    .ToList();

                if (hitsForThreshold.Count > 0)
                    hitRates[threshold] = hitsForThreshold.Count(h => h) / (double)hitsForThreshold.Count;
            }
        }

        return CalibrationExperimentResult.Create(
            experiment.ExperimentId, experiment.ConfigurationFingerprint, experiment.Dataset.Fingerprint,
            window, direction, CalibrationExperimentResultStatus.Succeeded,
            signalCount, positionCount, grossPnL, finalEquity, maxDrawdown, winRate, medianReturn, medianMfe, medianMae, hitRates);
    }

    /// <summary>A position's own <see cref="Execution.SimulatedPosition.Return"/> is not carried on
    /// <see cref="PositionRiskOutcome"/> - it is looked up from the matching <see cref="MeasurementResult"/>
    /// by <see cref="PositionRiskOutcome.PositionId"/>/<see cref="MeasurementResult.SignalBarIndex"/> (the
    /// SAME identifier space, per <see cref="Execution.SimulatedPosition.PositionId"/>'s own doc comment:
    /// "the candidate's own SignalBarIndex") - never recomputed.</summary>
    private static double? TryFindReturn(PositionRiskOutcome outcome, IReadOnlyList<MeasurementResult> measured) =>
        measured.FirstOrDefault(m => m.SignalBarIndex == outcome.PositionId)?.Return;

    private static bool IsDirectional(DirectionCandidate direction) =>
        direction is DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE;

    private static bool Matches(DirectionCandidate direction, CalibrationDirectionFilter filter) => filter switch
    {
        CalibrationDirectionFilter.All => true,
        CalibrationDirectionFilter.Buy => direction == DirectionCandidate.BUY_CANDIDATE,
        CalibrationDirectionFilter.Sell => direction == DirectionCandidate.SELL_CANDIDATE,
        _ => false
    };

    private static double? Median(IEnumerable<double> values)
    {
        List<double> sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0)
            return null;

        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
