using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Calibration;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Backtest.Risk;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests.Yahoo;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.13). INTEGRATION / NETWORK. Constrained, mono-window sensitivity revalidation of
/// <see cref="EntryTriggerBuilder.AmbiguityGateThreshold"/> (production value 0.95, unchanged by this test)
/// on the maximum-depth MES=F/M5 Yahoo dataset (Lot 14.12 recipe: <see cref="YahooHistoricalBarSource.DefaultMaxChunkSpanDays"/>).
///
/// Reuses the real pipeline exclusively: every configuration is a <see cref="CalibrationExperiment"/> run
/// through <see cref="CalibrationExperimentRunner"/> (Lot 14.9/14.10, unmodified) for the official
/// TRAIN/VALIDATION/OOS x ALL/BUY/SELL breakdown, plus one direct <see cref="BacktestEngine.RunFullBacktestWithRisk"/>
/// call per threshold (the SAME production method <see cref="CalibrationExperimentRunner"/> itself calls) to
/// join already-computed per-bar <c>Decision.Winner</c>/<c>EntryTrigger.Assessment</c> fields into a
/// regime-conditional / gate-rejection breakdown the framework does not natively slice by (Lot 14.11 D14.11-2:
/// Decision.Winner is not carried on MeasurementResult/PositionRiskOutcome). No value is recomputed - every
/// number tabulated here already exists on <see cref="BacktestSignalResult"/>/<see cref="MeasurementResult"/>/
/// <see cref="Risk.PositionRiskOutcome"/>, this file only groups/counts them (brief §17 "NO MANUAL
/// RECOMPUTATION").
///
/// NEVER selects a "best" threshold (brief §27) - every assertion below proves a FRAMEWORK PROPERTY
/// (binding/behavioural-effect/determinism/isolation/fingerprint), never a performance ranking.
/// </summary>
public sealed class AmbiguityGateThresholdRevalidationLot1413Tests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public AmbiguityGateThresholdRevalidationLot1413Tests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private static readonly decimal[] ThresholdGrid = { 0.80m, 0.85m, 0.90m, 0.925m, 0.95m, 0.975m, 0.99m };

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Integration_Network_AmbiguityGateThresholdRevalidation_Lot1413_SensitivityGrid()
    {
        try
        {
            // Sprint 15.25 (Lot 15.25-XX): consume the ONE shared session download instead of issuing
            // yet another identical 59-day MES/M5 request (brief Phase 7 - request minimization).
            HistoricalSeries series = _yahoo.Require();
            Assert.True(series.Count > 0);

            const int leadIn = 128;
            int remaining = series.Count - leadIn;
            if (remaining < 300)
            {
                Assert.Skip($"insufficient data: only {series.Count} bars returned for this lot's TRAIN/VALIDATION/OOS split.");
            }

            int train = (int)(remaining * 0.70);
            int validation = (int)(remaining * 0.15);
            int oos = remaining - train - validation - 1; // 1-bar safety margin below series.Count
            if (train < 1 || validation < 1 || oos < 1)
            {
                Assert.Skip("insufficient data: remaining bars too few to give every window at least one bar.");
            }

            CalibrationDatasetSpecification spec = CalibrationDatasetSpecification.Create(
                "Yahoo", series.Symbol, series.TimeFrame, series.FirstTimestamp, series.LastTimestamp.AddMinutes(1));
            var dataset = new CalibrationDataset(spec, series);
            CalibrationWindowSet windows = CalibrationTestFixtures.Windows(dataset, leadIn, train, validation, oos);

            CalibrationExperimentSetup setup = new()
            {
                WarmupContract = new CalibrationWarmupContract(leadIn),
                Instrument = Spec(),
                Policy = Policy(),
                InitialCapital = 50_000m,
                MeasurementConfiguration = MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 }),
                ExecutionConfiguration = ExecutionConfiguration.Create(10),
                PnLConfiguration = PnLConfiguration.Create(InstrumentPnLSpecification.Create("MES", priceUnitValue: 5m, currency: "USD"), quantity: 1),
                CostConfiguration = ExecutionCostConfiguration.Disabled(),
                RiskConfiguration = BacktestRiskConfiguration.Disabled()
            };

            _output.WriteLine("=== DATASET IDENTITY (Lot 14.13, THIS run - Yahoo window is rolling, see Lot 14.12 report §23) ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, Provider=Yahoo, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");
            _output.WriteLine($"DatasetFingerprint={dataset.Fingerprint}");
            _output.WriteLine($"TRAIN=[{windows.Train.Start:O}..{windows.Train.End:O}) ({train} bars)");
            _output.WriteLine($"VALIDATION=[{windows.Validation.Start:O}..{windows.Validation.End:O}) ({validation} bars)");
            _output.WriteLine($"OOS=[{windows.Oos.Start:O}..{windows.Oos.End:O}) ({oos} bars) - DESCRIPTIVE ONLY, never used to select a threshold (brief §13).");

            // ── Build the deterministic grid (brief §3/§7 - CalibrationGrid, never a hand-picked "best") ──
            CalibrationGridAxis axis = CalibrationGridAxis.Create(
                ThresholdGrid.Select(t => CalibrationParameter.Decimal(
                    CalibrationParameterBinding.AmbiguityGateThresholdParameterName, t, "score", min: 0m, max: 1m)).ToArray());
            IReadOnlyList<CalibrationParameterSet> parameterSets = CalibrationGrid.GenerateParameterSets(new[] { axis });
            Assert.Equal(ThresholdGrid.Length, parameterSets.Count);
            Assert.Contains(0.95m, ThresholdGrid); // brief §3: 0.95 MUST be present

            var fullSpan = new BacktestWindow("LOT14.13-FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1));
            BacktestScenario scenario = BacktestScenario.Create(series, fullSpan, setup.InitialCapital, setup.Instrument, setup.Policy);

            var experiments = new Dictionary<decimal, CalibrationExperiment>();
            var runResults = new Dictionary<decimal, CalibrationExperimentRunResult>();
            var fullResults = new Dictionary<decimal, BacktestFullResultWithRisk>();

            foreach (CalibrationParameterSet ps in parameterSets)
            {
                decimal threshold = ps.TryGet(CalibrationParameterBinding.AmbiguityGateThresholdParameterName)!.DecimalValue!.Value;

                CalibrationExperiment experiment = CalibrationExperiment.Create(ps, dataset, windows, setup);
                experiments[threshold] = experiment;
                runResults[threshold] = CalibrationExperimentRunner.Run(experiment);

                PipelineParameterOverrides overrides = CalibrationParameterBinding.Resolve(ps);
                fullResults[threshold] = new BacktestEngine().RunFullBacktestWithRisk(
                    scenario, leadIn, setup.MeasurementConfiguration, setup.ExecutionConfiguration,
                    setup.PnLConfiguration, setup.CostConfiguration, setup.RiskConfiguration, overrides);
            }

            // ══════════════════════════ FRAMEWORK-PROPERTY ASSERTIONS (brief §26/§30) ══════════════════════════

            // (1) PARAMETER BINDING + FINGERPRINT: distinct thresholds -> distinct ConfigurationFingerprint.
            List<string> fingerprints = experiments.Values.Select(e => e.ConfigurationFingerprint).ToList();
            Assert.Equal(fingerprints.Count, fingerprints.Distinct().Count());

            // (2) BEHAVIOURAL EFFECT: the two extremes must produce an actually different observable count
            // somewhere in TRAIN/ALL - never merely a different fingerprint (Lot 14.11 D14.11-1's own trap).
            CalibrationExperimentResult loTrainAll = runResults[0.80m].Results.Single(r => r.Window.Role == CalibrationWindowRole.Train && r.Direction == CalibrationDirectionFilter.All);
            CalibrationExperimentResult hiTrainAll = runResults[0.99m].Results.Single(r => r.Window.Role == CalibrationWindowRole.Train && r.Direction == CalibrationDirectionFilter.All);
            Assert.NotEqual(loTrainAll.SignalCount, hiTrainAll.SignalCount);

            // (3) DETERMINISM: an independently-built second experiment for the SAME threshold (0.95) must
            // reproduce identical Id/fingerprint/every per-slice ResultFingerprint.
            CalibrationParameterSet ps95Again = CalibrationParameterSet.Create(new[]
            {
                CalibrationParameter.Decimal(CalibrationParameterBinding.AmbiguityGateThresholdParameterName, 0.95m, "score", 0m, 1m)
            });
            CalibrationExperiment exp95Again = CalibrationExperiment.Create(ps95Again, dataset, windows, setup);
            CalibrationExperimentRunResult run95Again = CalibrationExperimentRunner.Run(exp95Again);
            Assert.Equal(experiments[0.95m].ExperimentId, exp95Again.ExperimentId);
            Assert.Equal(experiments[0.95m].ConfigurationFingerprint, exp95Again.ConfigurationFingerprint);
            for (int i = 0; i < runResults[0.95m].Results.Count; i++)
                Assert.Equal(runResults[0.95m].Results[i].ResultFingerprint, run95Again.Results[i].ResultFingerprint);

            // (4) RUN ISOLATION: A(0.80) -> B(0.95) -> C(0.99) -> B(0.95) in that literal sequence order -
            // B1 must equal B2, and A/B/C must pairwise differ (brief §19).
            CalibrationExperimentRunResult seqA = RunFresh(0.80m, dataset, windows, setup);
            CalibrationExperimentRunResult seqB1 = RunFresh(0.95m, dataset, windows, setup);
            CalibrationExperimentRunResult seqC = RunFresh(0.99m, dataset, windows, setup);
            CalibrationExperimentRunResult seqB2 = RunFresh(0.95m, dataset, windows, setup);
            Assert.Equal(seqB1.ConfigurationFingerprint, seqB2.ConfigurationFingerprint);
            for (int i = 0; i < seqB1.Results.Count; i++)
                Assert.Equal(seqB1.Results[i].ResultFingerprint, seqB2.Results[i].ResultFingerprint);
            Assert.NotEqual(seqA.ConfigurationFingerprint, seqB1.ConfigurationFingerprint);
            Assert.NotEqual(seqB1.ConfigurationFingerprint, seqC.ConfigurationFingerprint);
            Assert.NotEqual(seqA.ConfigurationFingerprint, seqC.ConfigurationFingerprint);

            // (5) LOOK-AHEAD / NO UPSTREAM CONTAMINATION: Decision.Winner (the regime classification) is
            // computed strictly upstream of AmbiguityGateThreshold (EntryTriggerBuilder.DetermineDirection,
            // read top-to-bottom: regime check precedes the ambiguity check) - the per-bar regime map must
            // therefore be IDENTICAL across every threshold in the grid. Structural look-ahead-safety itself
            // is already proven generically by Lot 14.9's CalibrationLookAheadTests (unmodified, still
            // enforced); this assertion is this lot's own confirmation that varying this one parameter does
            // not, in practice, disturb anything upstream of it.
            Dictionary<int, MarketState> regimeMap95 = BuildRegimeMap(fullResults[0.95m].SignalResult, windows.Train.Start);
            foreach (decimal threshold in ThresholdGrid)
            {
                if (threshold == 0.95m) continue;
                Dictionary<int, MarketState> regimeMapOther = BuildRegimeMap(fullResults[threshold].SignalResult, windows.Train.Start);
                Assert.Equal(regimeMap95.Count, regimeMapOther.Count);
                foreach (KeyValuePair<int, MarketState> kv in regimeMap95)
                    Assert.Equal(kv.Value, regimeMapOther[kv.Key]);
            }

            // (6) PRODUCTION VALUE UNCHANGED (brief §25/§27).
            Assert.Equal(0.95, EntryTriggerBuilder.AmbiguityGateThreshold);
            Assert.Equal(0.95m, ThresholdGrid.Single(t => t == 0.95m));

            _output.WriteLine("");
            _output.WriteLine("=== FRAMEWORK-PROPERTY CHECKS ===");
            _output.WriteLine("ParameterBinding=PASS (7/7 distinct ConfigurationFingerprint)");
            _output.WriteLine($"BehaviouralEffect=PASS (TRAIN/ALL SignalCount: 0.80={loTrainAll.SignalCount}, 0.99={hiTrainAll.SignalCount})");
            _output.WriteLine("Determinism=PASS (independent rebuild of threshold=0.95 reproduces identical Id/fingerprints)");
            _output.WriteLine("RunIsolation=PASS (A/B/C/B sequence: B1==B2, A/B/C pairwise distinct)");
            _output.WriteLine("Fingerprint=PASS (distinct threshold -> distinct ConfigurationFingerprint; same threshold -> identical)");
            _output.WriteLine("LookAhead=PASS (regime map identical across all 7 thresholds; structural proof: Lot 14.9 CalibrationLookAheadTests, unmodified)");
            _output.WriteLine("ProductionValueChanged=NO (EntryTriggerBuilder.AmbiguityGateThreshold const == 0.95, unread by this test's overrides path)");

            // ══════════════════════════ SIGNAL / MEASUREMENT / ECONOMIC LEVEL REPORT (brief §4) ══════════════════════════

            _output.WriteLine("");
            _output.WriteLine("=== OFFICIAL WINDOW x DIRECTION RESULTS (CalibrationExperimentRunner, 9 slices/threshold) ===");
            foreach (decimal threshold in ThresholdGrid)
            {
                foreach (CalibrationExperimentResult slice in runResults[threshold].Results)
                {
                    _output.WriteLine(
                        $"threshold={threshold:F3} {slice.Window.Role}/{slice.Direction}: status={slice.Status}, " +
                        $"signals={slice.SignalCount}, positions={slice.PositionCount}, " +
                        $"grossPnL={slice.GrossPnL}, finalEquity={slice.FinalEquity}, maxDrawdown={slice.MaximumDrawdown}, " +
                        $"winRate={slice.WinRate}, medianReturn={slice.MedianReturn}, medianMfe={slice.MedianMfe}, medianMae={slice.MedianMae}, " +
                        $"hitRates=[{string.Join(", ", slice.HitRates.Select(h => $"{h.Key}={h.Value:F4}"))}]");
                }
            }

            // ── Signal-level gate diagnostics (candidates / BUY / SELL / rejected-by-gate / acceptance rate),
            // TRAIN+VALIDATION combined (brief §4 SIGNAL LEVEL; OOS excluded from this deeper breakdown to
            // honour the "OOS untouched" spirit even though it is only descriptive - brief §13). ──
            _output.WriteLine("");
            _output.WriteLine("=== SIGNAL-LEVEL GATE DIAGNOSTICS (TRAIN+VALIDATION combined, whole-window) ===");
            foreach (decimal threshold in ThresholdGrid)
            {
                GateDiagnostics diag = TabulateGate(fullResults[threshold].SignalResult, windows.Train.Start, windows.Oos.Start);
                _output.WriteLine(
                    $"threshold={threshold:F3}: candidates(MeanReverting bars)={diag.Candidates}, BUY={diag.Buy}, SELL={diag.Sell}, " +
                    $"rejectedByAmbiguityGate={diag.RejectedByGate}, acceptanceRate={(diag.Candidates > 0 ? (double)(diag.Buy + diag.Sell) / diag.Candidates : (double?)null):F4}");
            }

            // ── Regime-conditional breakdown (brief §5), whole-window TRAIN+VALIDATION+OOS (descriptive for
            // OOS), joined via SignalBarIndex - never a new Regime Engine, only a grouping of already-computed
            // MeasurementResult/PositionRiskOutcome rows by the already-computed Decision.Winner. ──
            _output.WriteLine("");
            _output.WriteLine("=== REGIME-CONDITIONAL BREAKDOWN (whole 59-day window, descriptive; sample-size caveats in report) ===");
            Dictionary<int, MarketState> regimeMapFull = BuildRegimeMap(fullResults[0.95m].SignalResult, series.FirstTimestamp);
            foreach (decimal threshold in ThresholdGrid)
            {
                foreach (MarketState regime in new[] { MarketState.MeanReverting, MarketState.StructuralBreak, MarketState.Trending, MarketState.RandomWalk, MarketState.StableRange })
                {
                    RegimeSlice rslice = TabulateRegime(fullResults[threshold], regimeMapFull, regime, windows.Train.Start);
                    if (rslice.SignalCount == 0 && rslice.PositionCount == 0) continue;
                    _output.WriteLine(
                        $"threshold={threshold:F3} regime={regime}: signals={rslice.SignalCount}, buy={rslice.Buy}, sell={rslice.Sell}, " +
                        $"positions={rslice.PositionCount}, medianReturn={rslice.MedianReturn}, medianMfe={rslice.MedianMfe}, medianMae={rslice.MedianMae}");
                }
            }
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static CalibrationExperimentRunResult RunFresh(
        decimal threshold, CalibrationDataset dataset, CalibrationWindowSet windows, CalibrationExperimentSetup setup)
    {
        CalibrationParameterSet ps = CalibrationParameterSet.Create(new[]
        {
            CalibrationParameter.Decimal(CalibrationParameterBinding.AmbiguityGateThresholdParameterName, threshold, "score", 0m, 1m)
        });
        CalibrationExperiment experiment = CalibrationExperiment.Create(ps, dataset, windows, setup);
        return CalibrationExperimentRunner.Run(experiment);
    }

    /// <summary>BarIndex -&gt; Decision.Winner for every bar at/after <paramref name="from"/>, restricted to
    /// bars that actually reached a Decision (Ready/Warmup, never Rejected/Exception - matches Lot 14.12's own
    /// tabulation discipline).</summary>
    private static Dictionary<int, MarketState> BuildRegimeMap(BacktestSignalPipelineResult signalResult, DateTime from)
    {
        var map = new Dictionary<int, MarketState>();
        foreach (BacktestSignalResult bar in signalResult.Bars)
        {
            if (bar.Timestamp < from || bar.Decision is null) continue;
            map[bar.BarIndex] = bar.Decision.Winner;
        }
        return map;
    }

    private readonly record struct GateDiagnostics(int Candidates, int Buy, int Sell, int RejectedByGate);

    private static GateDiagnostics TabulateGate(BacktestSignalPipelineResult signalResult, DateTime from, DateTime to)
    {
        int candidates = 0, buy = 0, sell = 0, rejected = 0;
        foreach (BacktestSignalResult bar in signalResult.Bars)
        {
            if (bar.Timestamp < from || bar.Timestamp >= to) continue;
            if (bar.Decision?.Winner != MarketState.MeanReverting) continue;
            if (bar.EntryTrigger is null) continue;

            candidates++;
            switch (bar.EntryTrigger.Assessment.Direction)
            {
                case DirectionCandidate.BUY_CANDIDATE: buy++; break;
                case DirectionCandidate.SELL_CANDIDATE: sell++; break;
                default:
                    if (bar.EntryTrigger.Assessment.Reason == EntryTriggerReason.DECISION_AMBIGUOUS) rejected++;
                    break;
            }
        }
        return new GateDiagnostics(candidates, buy, sell, rejected);
    }

    private readonly record struct RegimeSlice(int SignalCount, int Buy, int Sell, int PositionCount, double? MedianReturn, double? MedianMfe, double? MedianMae);

    private static RegimeSlice TabulateRegime(BacktestFullResultWithRisk fullResult, Dictionary<int, MarketState> regimeMap, MarketState regime, DateTime from)
    {
        List<MeasurementResult> measurements = fullResult.Measurements
            .Where(m => m.SignalTimestamp >= from && regimeMap.TryGetValue(m.SignalBarIndex, out MarketState r) && r == regime)
            .Where(m => m.Direction is DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE)
            .ToList();

        int buy = measurements.Count(m => m.Direction == DirectionCandidate.BUY_CANDIDATE);
        int sell = measurements.Count(m => m.Direction == DirectionCandidate.SELL_CANDIDATE);

        List<PositionRiskOutcome> outcomes = fullResult.RiskResult.Outcomes
            .Where(o => o.Status == PositionStatus.Closed && o.EntryTimestamp >= from
                && regimeMap.TryGetValue(o.PositionId, out MarketState r) && r == regime
                && o.RiskEvaluation.IsAllowed && o.NetPnL is not null)
            .ToList();

        List<MeasurementResult> measured = measurements.Where(m => m.Status == MeasurementStatus.Measured).ToList();

        return new RegimeSlice(
            measurements.Count, buy, sell, outcomes.Count,
            Median(measured.Where(m => m.Return is not null).Select(m => m.Return!.Value)),
            Median(measured.Where(m => m.Mfe is not null).Select(m => m.Mfe!.Value)),
            Median(measured.Where(m => m.Mae is not null).Select(m => m.Mae!.Value)));
    }

    private static double? Median(IEnumerable<double> values)
    {
        List<double> sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return null;
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException;
}
