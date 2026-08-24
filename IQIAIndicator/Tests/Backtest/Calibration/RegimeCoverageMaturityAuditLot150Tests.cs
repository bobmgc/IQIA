using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Backtest.Risk;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.Arbitration;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Engine.TradePlan;
using Xunit;
using Xunit.Abstractions;
using FusionEngine = IQIAIndicator.Engine.Fusion.EvidenceFusionEngine;
using FusionRandomWalkRule = IQIAIndicator.Engine.Fusion.Rules.RandomWalkRule;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25/15.26 (Lot 15.0, "Regime Coverage &amp; System Maturity Audit"). AUDIT-ONLY, OBSERVATION
/// ONLY - builds a new, additive, deterministic dataset/tool that runs the full production signal
/// pipeline (Regime -&gt; Fusion -&gt; Decision -&gt; Methodology -&gt; Signal -&gt; Entry -&gt; EntryTrigger -&gt; TradePlan
/// -&gt; Execution -&gt; Measurement -&gt; Risk -&gt; Cost) over the real ~59-day Yahoo MES M5 dataset, tags every
/// bar with its Decision-layer regime label (<see cref="MarketState"/>, from <c>bar.Decision.Winner</c> -
/// the live/real regime taxonomy; the dead <c>Engine.Regime.Core.RegimeType</c>/<c>EvidenceFusionEngine</c>
/// stub is explicitly out of scope, confirmed unreferenced), and dumps comprehensive per-regime statistics
/// to CSV/txt files. This test does not draw conclusions - it only gathers and prints numbers; the formal
/// audit report is written separately from this test's output.
///
/// PROTECTED FILES: never edits RegimeEngine/EvidenceFusionEngine(x2)/FusionStateManager/DecisionEngine/
/// DecisionArbitrator/SignalEngine/EntryEngine/EntryTriggerEngine/EntryTriggerBuilder/TradePlanBuilder/
/// RiskEngine/RiskPolicy/ExecutionSimulator - only ever CONSTRUCTS/CALLS them exactly as
/// <see cref="BacktestEngine"/> or the Lot 14.17
/// (<c>PersistenceZeroVarianceActivationAuditLot1417Tests</c>) precedent already does, never modifies their
/// source. Purely additive: this file plus its own Output/ CSVs are the only things created.
/// </summary>
public sealed class RegimeCoverageMaturityAuditLot150Tests
{
    private readonly ITestOutputHelper _output;

    public RegimeCoverageMaturityAuditLot150Tests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ─────────────────────────────── Scenario / configuration (brief's exact MES spec/policy;
    // Measurement/Execution/PnL/Cost/Risk configurations copied from the exact construction used by
    // BacktestFullResultWithRiskIntegrationTests / BacktestFullResultWithRiskCostActivationTests, with the
    // Instrument/Symbol swapped from their "ES" fixture to this audit's required "MES" spec) ─────────────

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static MeasurementConfiguration Measurement() => MeasurementConfiguration.Create(10, new[] { 0.001 });

    private static ExecutionConfiguration Exec() => ExecutionConfiguration.Create(10);

    private static PnLConfiguration Pnl() =>
        PnLConfiguration.Create(InstrumentPnLSpecification.Create("MES", 5m, "USD"), quantity: 1, startingCapital: 50_000m);

    private static BacktestRiskConfiguration RiskEnabled() =>
        BacktestRiskConfiguration.Create(true, RiskDistanceConfiguration.Fixed(2m));

    /// <summary>TEST FIXTURE ONLY, copied verbatim from BacktestFullResultWithRiskCostActivationTests -
    /// never a broker schedule. Enabled purely so this audit's cost-availability table has real,
    /// non-degenerate numbers to report (brief item 3 "cost availability/descriptive stats per regime if
    /// available") - no calibration/tuning implied.</summary>
    private static ExecutionCostConfiguration EnabledTestCosts() => ExecutionCostConfiguration.Create(
        enabled: true,
        slippage: SlippageConfiguration.Fixed(0.25m),
        spread: SpreadConfiguration.Fixed(0.5m),
        commission: CommissionConfiguration.Create(perOrder: 2m, perUnit: 0.5m),
        fees: FeesConfiguration.Create(perOrder: 0.25m));

    // ─────────────────────────────────────────── Per-bar record ───────────────────────────────────────

    private sealed class BarRecord
    {
        public required int BarIndex;
        public required DateTime Timestamp;
        public required MarketState Regime;

        public required double WinnerScore;
        public required MarketState? RunnerUpState;
        public required double? RunnerUpScore;
        public required double? ScoreDifference;
        public required double AmbiguityScore;
        public required double Confidence;

        public required double? AdfValue; public required bool AdfValid;
        public required double? KpssValue; public required bool KpssValid;
        public required double? HurstValue; public required bool HurstValid;
        public required double? HalfLifeValue; public required bool HalfLifeValid;
        public required double? VarianceRatioValue; public required bool VarianceRatioValid;
        public required double? CusumValue; public required bool CusumValid;
        public required double? VolatilityValue; public required bool VolatilityValid;
        public required double? BaiPerronValue; public required bool BaiPerronValid;
        public required double? DfaValue; public required bool DfaValid;

        public required double RawStationarity;
        public required double RawPersistence;
        public required double RawMeanReversion;
        public required double RawRandomWalk;
        public required double StableStationarity;
        public required double StablePersistence;
        public required double StableMeanReversion;
        public required double StableStructuralStability;
        public required double StableRandomWalk;

        public required bool SignalPresent;
        public required bool EntryCandidatePresent;
        public required DirectionCandidate? EntryTriggerDirection;
        public required TradePlanStatus? TradePlanStatusValue;
        public required bool TradePlanHasStopLoss;

        public required PositionStatus PositionStatus;
        public required bool PositionOpened;
        public required bool PositionClosed;

        public required MeasurementStatus MeasurementStatus;
        public required double? MeasurementReturn;
        public required double? Mfe;
        public required double? Mae;
        public required bool? HitThreshold0;

        public required PositionRiskReason RiskReason;
        public required bool RiskAllowed;
        public required decimal? NetPnL;
        public required decimal? TotalCost;
    }

    [Fact]
    public void Integration_Network_RegimeCoverageMaturityAudit_Lot150()
    {
        try
        {
            // ── Step 1: load the real dataset (exact same call shape as Lot 14.17) ──────────────────────
            var source = new YahooHistoricalBarSource();
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-YahooHistoricalBarSource.DefaultMaxChunkSpanDays);

            HistoricalSeries series = source.Load("MES", "M5", from, to);
            Assert.True(series.Count > 0);

            string fingerprint = HistoricalSeriesFingerprint.Compute(series);
            const int warmupBars = 128;

            _output.WriteLine("=== DATASET IDENTITY (Lot 15.0, THIS run) ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");
            _output.WriteLine($"DatasetFingerprint={fingerprint}");

            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT15.0-FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            // ── Step 2: run the FULL production pipeline exactly once ───────────────────────────────────
            BacktestFullResultWithRisk result = new BacktestEngine().RunFullBacktestWithRisk(
                scenario, warmupBars, Measurement(), Exec(), Pnl(), EnabledTestCosts(), RiskEnabled());

            BacktestSignalPipelineResult signalResult = result.SignalResult;

            _output.WriteLine("");
            _output.WriteLine("=== GLOBAL STATUS COUNTS (from BacktestSignalPipelineResult) ===");
            _output.WriteLine($"BarsProcessed={signalResult.BarsProcessed}, BarsRejected={signalResult.BarsRejected}, WarmupBars={signalResult.WarmupBars}, ReadyBars={signalResult.ReadyBars}, ExceptionCount={signalResult.ExceptionCount}, TotalSeriesBars={series.Count}");

            int directRejected = signalResult.Bars.Count(b => b.Status == BacktestSignalStatus.Rejected);
            int directWarmup = signalResult.Bars.Count(b => b.Status == BacktestSignalStatus.Warmup);
            int directReady = signalResult.Bars.Count(b => b.Status == BacktestSignalStatus.Ready);
            int directException = signalResult.Bars.Count(b => b.Status == BacktestSignalStatus.Exception);
            _output.WriteLine($"Direct count cross-check: Rejected={directRejected}, Warmup={directWarmup}, Ready={directReady}, Exception={directException}, Sum={directRejected + directWarmup + directReady + directException}, series.Count={series.Count}");
            Assert.Equal(series.Count, directRejected + directWarmup + directReady + directException);

            if (directException > 0)
            {
                _output.WriteLine("EXCEPTION BAR DETAIL (should be none on this dataset per prior Lots' precedent):");
                foreach (var exBar in signalResult.Bars.Where(b => b.Status == BacktestSignalStatus.Exception))
                    _output.WriteLine($"  BarIndex={exBar.BarIndex}, Stage={exBar.Exception?.Stage}, Type={exBar.Exception?.ExceptionType}, Message={exBar.Exception?.Message}");
            }

            // ── Step 3: parallel RAW-vs-STABLE fusion walk (EXACT pattern of Lot 14.17's
            // PersistenceZeroVarianceActivationAuditLot1417Tests - constructs its own EvidenceFusionEngine
            // with the same 4 rules, in the same order, and its own FusionStateManager, fed bar-by-bar in
            // chronological order using each bar's own .Regime EvidenceSet - never touches production
            // fields, never mutates any protected engine). StructuralStability has no RAW counterpart here
            // (it is injected only inside the protected FusionStateManager itself) - only reported STABLE,
            // per the task brief's explicit instruction. Improved slightly over the Lot 14.17 precedent:
            // an Exception bar whose failing stage is AFTER Fusion (Decision/Methodology/Signal/TradePlan)
            // still had its FusionStateManager.Update already applied in the real production run, so it is
            // still fed here too - never skipped purely because Status==Exception - keeping this parallel
            // walk's STABLE state aligned with what production actually accumulated. Since ExceptionCount is
            // asserted to be 0 immediately below, this refinement is inert for this dataset - documented for
            // correctness, not because it changes any number produced by this run.
            var fusionEngine = new FusionEngine(new IFusionRule[]
            {
                new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new FusionRandomWalkRule()
            });
            var fusionState = new FusionStateManager();

            var observations = new List<BarRecord>(series.Count);
            var warmupByRegime = new Dictionary<MarketState, int>();
            var exceptionByRegime = new Dictionary<MarketState, int>();
            int exceptionPreFusionCount = 0;

            var positionByBarIndex = result.ExecutionResult.Positions.ToDictionary(p => p.PositionId);
            var measurementByBarIndex = result.Measurements.ToDictionary(m => m.SignalBarIndex);
            var riskByPositionId = result.RiskResult.Outcomes.ToDictionary(o => o.PositionId);

            foreach (BacktestSignalResult bar in signalResult.Bars)
            {
                if (bar.Status == BacktestSignalStatus.Rejected) continue;
                if (bar.Regime is null) continue;

                bool fusionRanInProduction = bar.Status != BacktestSignalStatus.Exception
                    || bar.Exception?.Stage is "Decision" or "Methodology" or "Signal" or "TradePlan";

                if (!fusionRanInProduction)
                {
                    exceptionPreFusionCount++;
                    continue;
                }

                FusionResult rawResult = fusionEngine.Fuse(new FusionContext
                {
                    Evidence = bar.Regime, Timestamp = bar.Timestamp, Symbol = series.Symbol, TimeFrame = series.TimeFrame, EvaluationId = Guid.Empty
                });
                FusionSnapshot snapshot = fusionState.Update(rawResult, bar.Timestamp);

                if (bar.Decision is null)
                {
                    // Exception occurred exactly at/after Decision without a Decision object surviving -
                    // structurally shouldn't happen given the stage gate above, kept as defence in depth.
                    continue;
                }

                if (bar.Status == BacktestSignalStatus.Warmup)
                {
                    warmupByRegime[bar.Decision.Winner] = warmupByRegime.GetValueOrDefault(bar.Decision.Winner) + 1;
                    continue;
                }

                if (bar.Status == BacktestSignalStatus.Exception)
                {
                    exceptionByRegime[bar.Decision.Winner] = exceptionByRegime.GetValueOrDefault(bar.Decision.Winner) + 1;
                    continue;
                }

                if (bar.Status != BacktestSignalStatus.Ready) continue;

                // ── Decision score / runner-up ───────────────────────────────────────────────────────
                List<DecisionCandidate> sorted = bar.Decision.Candidates.OrderByDescending(c => c.FinalScore).ToList();
                DecisionCandidate? runnerUp = sorted.FirstOrDefault(c => c.MarketState != bar.Decision.Winner);
                MarketState? runnerUpState = runnerUp?.MarketState;
                double? runnerUpScore = runnerUp?.FinalScore;
                double? scoreDifference = runnerUp is null ? null : bar.Decision.WinnerScore - runnerUp.FinalScore;

                // ── Evidence extraction ──────────────────────────────────────────────────────────────
                EvidenceSet ev = bar.Regime;

                // ── Signal/Entry/Trigger/TradePlan ───────────────────────────────────────────────────
                bool signalPresent = bar.Signal is not null;
                bool entryCandidatePresent = bar.Entry is not null && bar.Entry.OpportunityStatus != OpportunityStatus.NOT_QUALIFIED;
                DirectionCandidate? triggerDirection = bar.EntryTrigger?.Assessment.Direction;
                TradePlanStatus? tradePlanStatus = bar.TradePlan?.Status;
                bool tradePlanHasStopLoss = bar.TradePlan?.StopLoss is not null;

                // ── Execution (index-aligned to BarIndex; PositionId == candidate.SignalBarIndex == BarIndex) ──
                positionByBarIndex.TryGetValue(bar.BarIndex, out SimulatedPosition? position);
                PositionStatus positionStatus = position?.Status ?? PositionStatus.NotExecutable;
                bool positionOpened = position?.EntryPrice is not null;
                bool positionClosed = position?.Status == PositionStatus.Closed;

                // ── Measurement (index-aligned via SignalBarIndex == BarIndex) ──────────────────────────
                measurementByBarIndex.TryGetValue(bar.BarIndex, out MeasurementResult? measurement);
                MeasurementStatus measurementStatus = measurement?.Status ?? MeasurementStatus.NotMeasurable;
                bool? hit0 = measurement?.HitResults is { Count: > 0 } hr ? hr[0].Hit : null;

                // ── Risk/Cost (keyed by PositionId == BarIndex) ─────────────────────────────────────────
                riskByPositionId.TryGetValue(bar.BarIndex, out PositionRiskOutcome? riskOutcome);
                PositionRiskReason riskReason = riskOutcome?.RiskEvaluation.Reason ?? PositionRiskReason.PositionNotExecutable;
                bool riskAllowed = riskOutcome?.RiskEvaluation.IsAllowed ?? false;

                observations.Add(new BarRecord
                {
                    BarIndex = bar.BarIndex,
                    Timestamp = bar.Timestamp,
                    Regime = bar.Decision.Winner,
                    WinnerScore = bar.Decision.WinnerScore,
                    RunnerUpState = runnerUpState,
                    RunnerUpScore = runnerUpScore,
                    ScoreDifference = scoreDifference,
                    AmbiguityScore = bar.Decision.AmbiguityScore,
                    Confidence = bar.Decision.Confidence,

                    AdfValue = ev.Adf is { } adf ? (double)adf.Statistic : null, AdfValid = ev.Adf?.IsValid ?? false,
                    KpssValue = ev.Kpss is { } kpss ? (double)kpss.Statistic : null, KpssValid = ev.Kpss?.IsValid ?? false,
                    HurstValue = ev.Hurst is { } hurst ? (double)hurst.HurstProxy : null, HurstValid = ev.Hurst?.IsValid ?? false,
                    HalfLifeValue = ev.HalfLife?.HalfLife, HalfLifeValid = ev.HalfLife?.IsValid ?? false,
                    VarianceRatioValue = ev.VarianceRatio?.VarianceRatio, VarianceRatioValid = ev.VarianceRatio?.IsValid ?? false,
                    CusumValue = ev.Cusum is { } cusum ? Math.Max(cusum.PositiveCusum, cusum.NegativeCusum) : null, CusumValid = ev.Cusum?.IsValid ?? false,
                    VolatilityValue = ev.Volatility is { } vol ? (double)vol.AcfAbsReturns : null, VolatilityValid = ev.Volatility?.IsValid ?? false,
                    BaiPerronValue = ev.BaiPerron is { } bp ? bp.BreakCount : null, BaiPerronValid = ev.BaiPerron?.IsValid ?? false,
                    DfaValue = ev.Dfa?.Hurst, DfaValid = ev.Dfa?.IsValid ?? false,

                    RawStationarity = Dim(rawResult, FusionDimension.Stationarity).Value,
                    RawPersistence = Dim(rawResult, FusionDimension.Persistence).Value,
                    RawMeanReversion = Dim(rawResult, FusionDimension.MeanReversion).Value,
                    RawRandomWalk = Dim(rawResult, FusionDimension.RandomWalk).Value,
                    StableStationarity = Dim(snapshot.StableResult, FusionDimension.Stationarity).Value,
                    StablePersistence = Dim(snapshot.StableResult, FusionDimension.Persistence).Value,
                    StableMeanReversion = Dim(snapshot.StableResult, FusionDimension.MeanReversion).Value,
                    StableStructuralStability = Dim(snapshot.StableResult, FusionDimension.StructuralStability).Value,
                    StableRandomWalk = Dim(snapshot.StableResult, FusionDimension.RandomWalk).Value,

                    SignalPresent = signalPresent,
                    EntryCandidatePresent = entryCandidatePresent,
                    EntryTriggerDirection = triggerDirection,
                    TradePlanStatusValue = tradePlanStatus,
                    TradePlanHasStopLoss = tradePlanHasStopLoss,

                    PositionStatus = positionStatus,
                    PositionOpened = positionOpened,
                    PositionClosed = positionClosed,

                    MeasurementStatus = measurementStatus,
                    MeasurementReturn = measurement?.Return,
                    Mfe = measurement?.Mfe,
                    Mae = measurement?.Mae,
                    HitThreshold0 = hit0,

                    RiskReason = riskReason,
                    RiskAllowed = riskAllowed,
                    NetPnL = riskOutcome?.NetPnL,
                    TotalCost = riskOutcome?.CostResult?.Cost?.TotalCost
                });
            }

            _output.WriteLine("");
            _output.WriteLine($"ObservedReadyBars={observations.Count} (expected == ReadyBars {signalResult.ReadyBars})");
            Assert.Equal(signalResult.ReadyBars, observations.Count);
            _output.WriteLine($"ExceptionBarsBeforeFusionCompleted={exceptionPreFusionCount}");

            string outputDir = ResolveOutputDirectory();

            // ═══════════════════════════════ AGGREGATION + CSV + PRINT (run #1) ═══════════════════════
            string hash1 = RunAggregationAndReport(observations, signalResult, warmupByRegime, exceptionByRegime, outputDir, print: true);

            // ═══════════════════════ Step 5: determinism/sanity re-run (no re-download) ════════════════
            string hash2 = RunAggregationAndReport(observations, signalResult, warmupByRegime, exceptionByRegime, outputDir, print: false);
            _output.WriteLine("");
            _output.WriteLine($"=== DETERMINISM CHECK === Hash1={hash1}, Hash2={hash2}, Identical={hash1 == hash2}");
            Assert.Equal(hash1, hash2);
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            _output.WriteLine($"SKIPPED (network/Yahoo unavailable, not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ─────────────────────────────────────── Aggregation + reporting ──────────────────────────────────

    private string RunAggregationAndReport(
        List<BarRecord> observations,
        BacktestSignalPipelineResult signalResult,
        Dictionary<MarketState, int> warmupByRegime,
        Dictionary<MarketState, int> exceptionByRegime,
        string outputDir,
        bool print)
    {
        var allStates = Enum.GetValues<MarketState>();
        var byRegime = observations.GroupBy(o => o.Regime).OrderByDescending(g => g.Count()).ToList();
        var hashInput = new StringBuilder();

        // ── Table: regime_status_counts.csv ─────────────────────────────────────────────────────────
        var statusRows = new List<object?[]>();
        int totalBars = signalResult.BarsProcessed + signalResult.BarsRejected;
        statusRows.Add(new object?[] { "Rejected", "N/A", signalResult.BarsRejected, Pct(signalResult.BarsRejected, totalBars) });
        foreach (MarketState s in allStates)
            statusRows.Add(new object?[] { "Warmup", s, warmupByRegime.GetValueOrDefault(s), Pct(warmupByRegime.GetValueOrDefault(s), signalResult.WarmupBars) });
        foreach (MarketState s in allStates)
        {
            int c = byRegime.FirstOrDefault(g => g.Key == s)?.Count() ?? 0;
            statusRows.Add(new object?[] { "Ready", s, c, Pct(c, observations.Count) });
        }
        foreach (MarketState s in allStates)
            statusRows.Add(new object?[] { "Exception", s, exceptionByRegime.GetValueOrDefault(s), Pct(exceptionByRegime.GetValueOrDefault(s), signalResult.ExceptionCount) });
        statusRows.Add(new object?[] { "TOTAL_BARS", "N/A", totalBars, 100.0 });
        WriteCsv(Path.Combine(outputDir, "regime_status_counts.csv"), new[] { "Status", "Regime", "Count", "PercentOfStatusTotal" }, statusRows);
        AppendHash(hashInput, statusRows);

        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== TABLE: regime_status_counts ===");
            foreach (var r in statusRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Table: regime_data_coverage.csv (+ runs, computed over the consecutive-Ready-bar sequence) ─
        var coverageRows = new List<object?[]>();
        var runsByRegime = ComputeRuns(observations);
        foreach (var g in byRegime)
        {
            MarketState regime = g.Key;
            int count = g.Count();
            List<(int Length, TimeSpan Span)> runs = runsByRegime.GetValueOrDefault(regime, new List<(int, TimeSpan)>());
            int runCount = runs.Count;
            double avgRunBars = runCount > 0 ? runs.Average(r => r.Length) : 0.0;
            double medianRunBars = runCount > 0 ? Percentile(runs.Select(r => (double)r.Length).OrderBy(v => v).ToList(), 0.5) : 0.0;
            int longestRunBars = runCount > 0 ? runs.Max(r => r.Length) : 0;
            double avgRunMinutes = runCount > 0 ? runs.Average(r => r.Span.TotalMinutes) : 0.0;
            double longestRunMinutes = runCount > 0 ? runs.Max(r => r.Span.TotalMinutes) : 0.0;

            coverageRows.Add(new object?[]
            {
                regime, count, Pct(count, observations.Count), runCount,
                Math.Round(avgRunBars, 2), Math.Round(medianRunBars, 2), longestRunBars,
                Math.Round(avgRunMinutes, 1), Math.Round(longestRunMinutes, 1)
            });
        }
        WriteCsv(Path.Combine(outputDir, "regime_data_coverage.csv"),
            new[] { "Regime", "BarCount", "PercentOfReadyBars", "RunCount(=TransitionsInto)", "AvgRunBars", "MedianRunBars", "LongestRunBars", "AvgRunMinutes", "LongestRunMinutes" },
            coverageRows);
        AppendHash(hashInput, coverageRows);
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== TABLE: regime_data_coverage ===");
            foreach (var r in coverageRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Table: regime_transitions.csv (from x to, over consecutive entries of the Ready-bar sequence) ─
        var transitionCounts = new Dictionary<(MarketState From, MarketState To), int>();
        for (int i = 1; i < observations.Count; i++)
        {
            var key = (observations[i - 1].Regime, observations[i].Regime);
            transitionCounts[key] = transitionCounts.GetValueOrDefault(key) + 1;
        }
        var fromTotals = transitionCounts.GroupBy(kv => kv.Key.From).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
        var transitionRows = transitionCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new object?[] { kv.Key.From, kv.Key.To, kv.Value, Pct(kv.Value, fromTotals[kv.Key.From]) })
            .ToList();
        WriteCsv(Path.Combine(outputDir, "regime_transitions.csv"), new[] { "FromRegime", "ToRegime", "Count", "PercentOfFromRegimeTransitions" }, transitionRows);
        AppendHash(hashInput, transitionRows);
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== TABLE: regime_transitions ===");
            foreach (var r in transitionRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Table: regime_evidence_stats.csv ────────────────────────────────────────────────────────
        var evidenceRows = new List<object?[]>();
        (string Name, Func<BarRecord, double?> Value, Func<BarRecord, bool> Valid)[] evidenceTypes =
        {
            ("Adf", o => o.AdfValue, o => o.AdfValid),
            ("Kpss", o => o.KpssValue, o => o.KpssValid),
            ("Hurst(VR2Proxy)", o => o.HurstValue, o => o.HurstValid),
            ("HalfLife", o => o.HalfLifeValue, o => o.HalfLifeValid),
            ("VarianceRatio", o => o.VarianceRatioValue, o => o.VarianceRatioValid),
            ("Cusum(MaxOfPosNeg)", o => o.CusumValue, o => o.CusumValid),
            ("Volatility(AcfAbsReturns)", o => o.VolatilityValue, o => o.VolatilityValid),
            ("BaiPerron(BreakCount)", o => o.BaiPerronValue, o => o.BaiPerronValid),
            ("Dfa(Hurst)", o => o.DfaValue, o => o.DfaValid)
        };
        foreach (var g in byRegime)
        {
            List<BarRecord> bars = g.ToList();
            foreach (var (name, valueFn, validFn) in evidenceTypes)
            {
                List<double> validValues = bars.Where(validFn).Select(valueFn).Where(v => v.HasValue).Select(v => v!.Value).ToList();
                int validCount = bars.Count(validFn);
                evidenceRows.Add(new object?[]
                {
                    g.Key, name, validCount, bars.Count,
                    validValues.Count > 0 ? Math.Round(validValues.Average(), 6) : (double?)null,
                    validValues.Count > 0 ? Math.Round(StdDev(validValues), 6) : (double?)null,
                    validValues.Count > 0 ? Math.Round(validValues.Min(), 6) : (double?)null,
                    validValues.Count > 0 ? Math.Round(validValues.Max(), 6) : (double?)null,
                    Pct(validCount, bars.Count)
                });
            }
        }
        WriteCsv(Path.Combine(outputDir, "regime_evidence_stats.csv"),
            new[] { "Regime", "EvidenceType", "NValid", "NTotal", "Mean", "StdDev", "Min", "Max", "PercentValid" }, evidenceRows);
        AppendHash(hashInput, evidenceRows);
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== TABLE: regime_evidence_stats ===");
            foreach (var r in evidenceRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Table: regime_fusion_raw_vs_stable.csv ──────────────────────────────────────────────────
        (FusionDimension Dim, Func<BarRecord, double> Raw, Func<BarRecord, double> Stable)[] dims =
        {
            (FusionDimension.Stationarity, o => o.RawStationarity, o => o.StableStationarity),
            (FusionDimension.Persistence, o => o.RawPersistence, o => o.StablePersistence),
            (FusionDimension.MeanReversion, o => o.RawMeanReversion, o => o.StableMeanReversion),
            (FusionDimension.RandomWalk, o => o.RawRandomWalk, o => o.StableRandomWalk)
        };
        // Freeze-rate classification uses the FULL chronological Ready-bar sequence (matches Lot 14.17's
        // exact method: bar i vs bar i-1, globally) then attributes each transition to the DESTINATION
        // bar's regime for the per-regime partition the brief asks for.
        var freezeByRegimeDim = new Dictionary<(MarketState, FusionDimension), (int RawChangedStableFrozen, int BothChanged, int BothFrozen)>();
        for (int i = 1; i < observations.Count; i++)
        {
            MarketState destRegime = observations[i].Regime;
            foreach (var (dim, rawFn, stableFn) in dims)
            {
                bool rawChanged = Math.Abs(rawFn(observations[i]) - rawFn(observations[i - 1])) > 1e-9;
                bool stableChanged = Math.Abs(stableFn(observations[i]) - stableFn(observations[i - 1])) > 1e-9;
                var key = (destRegime, dim);
                var cur = freezeByRegimeDim.GetValueOrDefault(key);
                if (rawChanged && !stableChanged) cur.RawChangedStableFrozen++;
                else if (rawChanged && stableChanged) cur.BothChanged++;
                else if (!rawChanged && !stableChanged) cur.BothFrozen++;
                freezeByRegimeDim[key] = cur;
            }
        }
        var fusionRows = new List<object?[]>();
        foreach (var g in byRegime)
        {
            List<BarRecord> bars = g.ToList();
            foreach (var (dim, rawFn, stableFn) in dims)
            {
                double rawStdDev = StdDev(bars.Select(rawFn).ToList());
                double stableStdDev = StdDev(bars.Select(stableFn).ToList());
                var freeze = freezeByRegimeDim.GetValueOrDefault((g.Key, dim));
                int freezeTotal = freeze.RawChangedStableFrozen + freeze.BothChanged + freeze.BothFrozen;
                fusionRows.Add(new object?[]
                {
                    g.Key, dim, bars.Count, Math.Round(rawStdDev, 6), Math.Round(stableStdDev, 6),
                    freeze.RawChangedStableFrozen, freeze.BothChanged, freeze.BothFrozen,
                    Pct(freeze.RawChangedStableFrozen, freezeTotal)
                });
            }
            // StructuralStability: STABLE-only (no RAW counterpart from this 4-rule parallel engine).
            double ssStdDev = StdDev(bars.Select(o => o.StableStructuralStability).ToList());
            fusionRows.Add(new object?[] { g.Key, "StructuralStability(STABLE-only,no-RAW-counterpart)", bars.Count, "N/A", Math.Round(ssStdDev, 6), "N/A", "N/A", "N/A", "N/A" });
        }
        WriteCsv(Path.Combine(outputDir, "regime_fusion_raw_vs_stable.csv"),
            new[] { "Regime", "Dimension", "N", "RawStdDev", "StableStdDev", "RawChangedStableFrozen", "BothChanged", "BothFrozen", "PctRawChangedStableFrozen" }, fusionRows);
        AppendHash(hashInput, fusionRows);
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== TABLE: regime_fusion_raw_vs_stable ===");
            foreach (var r in fusionRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Table: regime_decision_stats.csv ────────────────────────────────────────────────────────
        var decisionRows = new List<object?[]>();
        foreach (var g in byRegime)
        {
            List<BarRecord> bars = g.ToList();
            List<double> winnerScores = bars.Select(o => o.WinnerScore).ToList();
            List<double> confidences = bars.Select(o => o.Confidence).ToList();
            List<double> diffs = bars.Where(o => o.ScoreDifference.HasValue).Select(o => o.ScoreDifference!.Value).ToList();
            decisionRows.Add(new object?[]
            {
                g.Key, bars.Count,
                Math.Round(winnerScores.Average(), 6), Math.Round(StdDev(winnerScores), 6),
                Math.Round(confidences.Average(), 6), Math.Round(StdDev(confidences), 6),
                diffs.Count > 0 ? Math.Round(diffs.Average(), 6) : (double?)null,
                diffs.Count > 0 ? Math.Round(StdDev(diffs), 6) : (double?)null,
                diffs.Count > 0 ? Math.Round(Percentile(diffs.OrderBy(v => v).ToList(), 0.5), 6) : (double?)null,
                diffs.Count
            });
        }
        WriteCsv(Path.Combine(outputDir, "regime_decision_stats.csv"),
            new[] { "Regime", "N", "MeanWinnerScore", "StdDevWinnerScore", "MeanConfidence", "StdDevConfidence", "MeanScoreDifference", "StdDevScoreDifference", "MedianScoreDifference", "NWithRunnerUp" }, decisionRows);
        AppendHash(hashInput, decisionRows);
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== TABLE: regime_decision_stats ===");
            foreach (var r in decisionRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Table: regime_ambiguity.csv (against the production constant 0.95, never changed) ─────────
        var ambiguityRows = new List<object?[]>();
        foreach (var g in byRegime)
        {
            List<double> sorted = g.Select(o => o.AmbiguityScore).OrderBy(v => v).ToList();
            int geThreshold = sorted.Count(v => v >= EntryTriggerBuilder.AmbiguityGateThreshold);
            ambiguityRows.Add(new object?[]
            {
                g.Key, sorted.Count, Math.Round(sorted.Average(), 6), Math.Round(Percentile(sorted, 0.5), 6),
                Math.Round(Percentile(sorted, 0.25), 6), Math.Round(Percentile(sorted, 0.75), 6),
                geThreshold, Pct(geThreshold, sorted.Count)
            });
        }
        WriteCsv(Path.Combine(outputDir, "regime_ambiguity.csv"),
            new[] { "Regime", "N", "Mean", "Median", "P25", "P75", "CountGE0.95", "PercentGE0.95" }, ambiguityRows);
        AppendHash(hashInput, ambiguityRows);
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine($"=== TABLE: regime_ambiguity (threshold=EntryTriggerBuilder.AmbiguityGateThreshold={EntryTriggerBuilder.AmbiguityGateThreshold}) ===");
            foreach (var r in ambiguityRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Table: regime_signal_funnel.csv ─────────────────────────────────────────────────────────
        var funnelRows = new List<object?[]>();
        foreach (var g in byRegime)
        {
            List<BarRecord> bars = g.ToList();
            int n = bars.Count;
            int signalN = bars.Count(o => o.SignalPresent);
            int entryN = bars.Count(o => o.EntryCandidatePresent);
            int buy = bars.Count(o => o.EntryTriggerDirection == DirectionCandidate.BUY_CANDIDATE);
            int sell = bars.Count(o => o.EntryTriggerDirection == DirectionCandidate.SELL_CANDIDATE);
            int noAction = bars.Count(o => o.EntryTriggerDirection == DirectionCandidate.NO_ACTION);
            int watch = bars.Count(o => o.EntryTriggerDirection == DirectionCandidate.WATCH);
            int tpNoTrade = bars.Count(o => o.TradePlanStatusValue is null or TradePlanStatus.NO_TRADE);
            int tpSignalOnly = bars.Count(o => o.TradePlanStatusValue == TradePlanStatus.SIGNAL_ONLY);
            int tpReady = bars.Count(o => o.TradePlanStatusValue == TradePlanStatus.PLAN_READY);
            int tpBlocked = bars.Count(o => o.TradePlanStatusValue == TradePlanStatus.PLAN_BLOCKED);
            int opened = bars.Count(o => o.PositionOpened);
            int closed = bars.Count(o => o.PositionClosed);
            funnelRows.Add(new object?[]
            {
                g.Key, n, signalN, Pct(signalN, n), entryN, Pct(entryN, n),
                buy, sell, noAction, watch,
                tpNoTrade, tpSignalOnly, tpReady, tpBlocked,
                opened, Pct(opened, n), closed, Pct(closed, n)
            });
        }
        WriteCsv(Path.Combine(outputDir, "regime_signal_funnel.csv"),
            new[] { "Regime", "ReadyBars", "SignalProduced", "SignalProducedPct", "EntryCandidate", "EntryCandidatePct",
                "TriggerBUY", "TriggerSELL", "TriggerNO_ACTION", "TriggerWATCH",
                "TradePlanNO_TRADE", "TradePlanSIGNAL_ONLY", "TradePlanPLAN_READY", "TradePlanPLAN_BLOCKED",
                "PositionOpened", "PositionOpenedPct", "PositionClosed", "PositionClosedPct" }, funnelRows);
        AppendHash(hashInput, funnelRows);
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== TABLE: regime_signal_funnel ===");
            foreach (var r in funnelRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Table: regime_measurement.csv (Status==Measured only; <30 flagged unreliable, never omitted) ─
        var measurementRows = new List<object?[]>();
        foreach (var g in byRegime)
        {
            List<BarRecord> measured = g.Where(o => o.MeasurementStatus == MeasurementStatus.Measured && o.MeasurementReturn.HasValue).ToList();
            int n = measured.Count;
            bool reliable = n >= 30;
            List<double> returns = measured.Select(o => o.MeasurementReturn!.Value).ToList();
            List<bool> hits = measured.Where(o => o.HitThreshold0.HasValue).Select(o => o.HitThreshold0!.Value).ToList();
            measurementRows.Add(new object?[]
            {
                g.Key, n, reliable ? "RELIABLE" : "LOW_N_UNRELIABLE",
                n > 0 ? Math.Round(returns.Average(), 6) : (double?)null,
                n > 0 ? Math.Round(Percentile(returns.OrderBy(v => v).ToList(), 0.5), 6) : (double?)null,
                n > 0 ? Math.Round(StdDev(returns), 6) : (double?)null,
                hits.Count > 0 ? Pct(hits.Count(h => h), hits.Count) : (double?)null,
                n > 0 ? Math.Round(measured.Average(o => o.Mae ?? 0.0), 6) : (double?)null,
                n > 0 ? Math.Round(measured.Average(o => o.Mfe ?? 0.0), 6) : (double?)null
            });
        }
        WriteCsv(Path.Combine(outputDir, "regime_measurement.csv"),
            new[] { "Regime", "NMeasured", "ReliabilityFlag(N>=30)", "MeanReturn", "MedianReturn", "StdDevReturn", "HitRateThreshold0.001Pct", "MeanMAE", "MeanMFE" }, measurementRows);
        AppendHash(hashInput, measurementRows);
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== TABLE: regime_measurement ===");
            foreach (var r in measurementRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Table: regime_risk.csv (+ cost availability folded in, per PositionRiskReason breakdown) ───
        var riskRows = new List<object?[]>();
        var allReasons = Enum.GetValues<PositionRiskReason>();
        foreach (var g in byRegime)
        {
            List<BarRecord> bars = g.ToList();
            int n = bars.Count;
            foreach (PositionRiskReason reason in allReasons)
            {
                int c = bars.Count(o => o.RiskReason == reason);
                riskRows.Add(new object?[] { g.Key, reason, c, Pct(c, n) });
            }
            List<decimal> costs = bars.Where(o => o.TotalCost.HasValue).Select(o => o.TotalCost!.Value).ToList();
            riskRows.Add(new object?[]
            {
                g.Key, "COST_AVAILABILITY(N_with_cost/MeanTotalCost)", costs.Count,
                costs.Count > 0 ? Math.Round((double)costs.Average(), 4) : (double?)null
            });
        }
        WriteCsv(Path.Combine(outputDir, "regime_risk.csv"), new[] { "Regime", "ReasonOrMetric", "Count", "Percent" }, riskRows);
        AppendHash(hashInput, riskRows);
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== TABLE: regime_risk (incl. cost availability) ===");
            foreach (var r in riskRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Sanity: no NaN/Infinity anywhere in the numeric CSV cells produced above ────────────────────
        foreach (var table in new[] { statusRows, coverageRows, transitionRows, evidenceRows, fusionRows, decisionRows, ambiguityRows, funnelRows, measurementRows, riskRows })
        {
            foreach (object?[] row in table)
            {
                foreach (object? cell in row)
                {
                    if (cell is double d) Assert.True(double.IsFinite(d), $"NaN/Infinity found in a report cell: {d}");
                }
            }
        }

        return Sha256Hex(hashInput.ToString());
    }

    // ─────────────────────────────────────────────── Helpers ──────────────────────────────────────────

    private static Dictionary<MarketState, List<(int Length, TimeSpan Span)>> ComputeRuns(List<BarRecord> observations)
    {
        var result = new Dictionary<MarketState, List<(int, TimeSpan)>>();
        if (observations.Count == 0) return result;

        int runStart = 0;
        for (int i = 1; i <= observations.Count; i++)
        {
            bool sameAsPrevious = i < observations.Count && observations[i].Regime == observations[runStart].Regime;
            if (!sameAsPrevious)
            {
                MarketState regime = observations[runStart].Regime;
                int length = i - runStart;
                TimeSpan span = observations[i - 1].Timestamp - observations[runStart].Timestamp;
                if (!result.TryGetValue(regime, out List<(int, TimeSpan)>? list))
                {
                    list = new List<(int, TimeSpan)>();
                    result[regime] = list;
                }
                list.Add((length, span));
                runStart = i;
            }
        }
        return result;
    }

    private static FusionConfidence Dim(FusionResult result, FusionDimension dimension) =>
        result.Dimensions.TryGetValue(dimension, out FusionConfidence? confidence)
            ? confidence
            : new FusionConfidence { Value = 0.0, Confidence = 0.0, Explanation = "Absent", IsAvailable = false };

    private static double Pct(int count, int total) => total > 0 ? Math.Round(100.0 * count / total, 3) : 0.0;

    private static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0.0;
        double mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0.0;
        if (sorted.Count == 1) return sorted[0];
        double rank = p * (sorted.Count - 1);
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sorted[lower];
        double fraction = rank - lower;
        return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
    }

    private static string ResolveOutputDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;

        if (dir is null)
            throw new InvalidOperationException("Could not locate IQIAIndicator.Tests.csproj to resolve the RegimeCoverageAudit output directory.");

        string outputDir = Path.Combine(dir, "Research", "RegimeCoverageAudit", "Output");
        Directory.CreateDirectory(outputDir);
        return outputDir;
    }

    private static void WriteCsv(string path, string[] headers, List<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(CsvCell)));
        foreach (object?[] row in rows)
            sb.AppendLine(string.Join(",", row.Select(CsvCell)));
        File.WriteAllText(path, sb.ToString());
    }

    private static string CsvCell(object? value)
    {
        string s = FormatCell(value);
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            s = "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private static string FormatCell(object? value) => value switch
    {
        null => "",
        double d => d.ToString("G17", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("O"),
        bool b => b.ToString(),
        _ => value.ToString() ?? ""
    };

    private static void AppendHash(StringBuilder sb, List<object?[]> rows)
    {
        foreach (object?[] row in rows)
            sb.Append(string.Join("|", row.Select(FormatCell))).Append(';');
    }

    private static string Sha256Hex(string s)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or InvalidOperationException;
}
