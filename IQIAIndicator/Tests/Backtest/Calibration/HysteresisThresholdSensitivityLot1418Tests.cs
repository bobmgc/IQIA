using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.Rules;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Fusion;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests.Calibration.HysteresisSensitivity;
using Xunit;
using IQIAIndicator.Tests.BacktestTests.Yahoo;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.18). INTEGRATION / NETWORK, DESCRIPTIVE SENSITIVITY STUDY - NOT CALIBRATION.
///
/// Studies how <c>FusionStateManager</c>'s <c>HysteresisThreshold</c> (production value 0.03, identified as
/// the technical freeze mechanism in Lot 14.17) affects Persistence's update frequency and frozen-run
/// lengths across a grid of candidate values. The real threshold is NEVER modified anywhere in this file or
/// elsewhere in production code - every non-production value is obtained exclusively through
/// <see cref="PersistenceHysteresisReplica"/>, a local, test-only, per-call object with no static/shared
/// mutable state (brief §26 "aucun état global, aucun singleton mutable, aucune reflection opaque"), whose
/// fidelity to the real <c>FusionStateManager</c> at production Alpha/Threshold is proven by
/// <c>HysteresisThresholdSensitivitySyntheticTests.ThresholdBinding_ReplicaMatchesRealFusionStateManager_AtProductionValues</c>.
///
/// Dataset acquisition mirrors Lot 14.17/14.12 exactly (same symbol/timeframe/window recipe -
/// <see cref="YahooHistoricalBarSource.DefaultMaxChunkSpanDays"/>=59 rolling days from "now"). As documented
/// project-wide, Yahoo's rolling window means the exact bar set (and therefore dataset fingerprint) differs
/// run-to-run - this is expected numeric drift, not a regression (see Lot 14.9-14.12 reports).
/// </summary>
public sealed class HysteresisThresholdSensitivityLot1418Tests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    // Grid centered on production's 0.03 (brief §3), fine enough near 0.03 to detect a transition.
    // 0.00 is semantically valid: with the production comparison `>=` (FusionStateManager.cs:117/118), a
    // threshold of exactly 0.00 is satisfied by any nonzero smoothed delta, which for continuous
    // floating-point RAW input is true on virtually every bar - it is the well-defined mathematical limit
    // where hysteresis has no suppressive effect (pure EMA(alpha=0.20), no additional freeze), NOT a
    // circumvention of the mechanism's contract. This is verified empirically below (§UpdateRate at 0.00).
    private static readonly double[] ThresholdGrid = { 0.00, 0.005, 0.01, 0.02, 0.03, 0.04, 0.05, 0.075, 0.10 };
    private const double ProductionThreshold = 0.03;
    private const double ProductionAlpha = 0.20;

    public HysteresisThresholdSensitivityLot1418Tests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private sealed class BarObservation
    {
        public required int BarIndex { get; init; }
        public required DateTime Timestamp { get; init; }
        public required MarketState Winner { get; init; }
        public required DirectionCandidate? EntryDirection { get; init; }
        public required FusionResult ProductionStableResult { get; init; }
        public required double PersistenceRaw { get; init; }
        public required double PersistenceStableProduction { get; init; }
    }

    [Fact]
    public void Integration_Network_HysteresisThresholdSensitivity_Lot1418()
    {
        try
        {
            HistoricalSeries series = _yahoo.Require();
            Assert.True(series.Count > 0);

            string fingerprint = HistoricalSeriesFingerprint.Compute(series);
            const int warmupBars = 128;

            _output.WriteLine("=== DATASET IDENTITY (Lot 14.18, THIS run) ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");
            _output.WriteLine($"DatasetFingerprint={fingerprint}");

            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT14.18-FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            BacktestSignalPipelineResult signalResult = new BacktestEngine().RunSignalPipeline(scenario, warmupBars);
            Assert.Equal(0, signalResult.ExceptionCount);

            var fusionEngine = new EvidenceFusionEngine(new IFusionRule[]
            {
                new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new Engine.Fusion.Rules.RandomWalkRule()
            });
            var fusionState = new FusionStateManager();

            var observations = new List<BarObservation>(series.Count);

            foreach (BacktestSignalResult bar in signalResult.Bars)
            {
                if (bar.Status is BacktestSignalStatus.Rejected or BacktestSignalStatus.Exception) continue;
                if (bar.Regime is null || bar.Decision is null) continue;

                FusionResult rawResult = fusionEngine.Fuse(new FusionContext
                {
                    Evidence = bar.Regime, Timestamp = bar.Timestamp, Symbol = series.Symbol, TimeFrame = series.TimeFrame, EvaluationId = Guid.Empty
                });
                double persistenceRaw = Dim(rawResult, FusionDimension.Persistence).Value;

                FusionSnapshot snapshot = fusionState.Update(rawResult, bar.Timestamp);

                if (bar.Status != BacktestSignalStatus.Ready) continue;

                observations.Add(new BarObservation
                {
                    BarIndex = bar.BarIndex,
                    Timestamp = bar.Timestamp,
                    Winner = bar.Decision.Winner,
                    EntryDirection = bar.EntryTrigger?.Assessment.Direction,
                    ProductionStableResult = snapshot.StableResult,
                    PersistenceRaw = persistenceRaw,
                    PersistenceStableProduction = Dim(snapshot.StableResult, FusionDimension.Persistence).Value
                });
            }

            _output.WriteLine($"ObservedBars(Ready)={observations.Count}");
            Assert.True(observations.Count > 1000, "Need a meaningfully sized dataset for percentile/regime/direction breakdowns.");

            double[] rawSequence = observations.Select(o => o.PersistenceRaw).ToArray();

            // ══════════════════════ §21 DETERMINISM (on the real captured sequence) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== DETERMINISM CHECK (production threshold, real dataset, replica run twice) ===");
            double[] det1 = RunReplica(rawSequence, ProductionThreshold);
            double[] det2 = RunReplica(rawSequence, ProductionThreshold);
            bool deterministic = det1.SequenceEqual(det2);
            _output.WriteLine($"DeterminismPass={deterministic}");
            Assert.True(deterministic, "Same dataset + same threshold must produce a bit-identical stable sequence.");

            // ══════════════════════ threshold binding fidelity vs REAL FusionStateManager ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== FIDELITY CHECK (replica @ 0.03/0.20 vs REAL FusionStateManager, real dataset) ===");
            double[] replicaAtProduction = RunReplica(rawSequence, ProductionThreshold);
            double[] realProduction = observations.Select(o => o.PersistenceStableProduction).ToArray();
            bool fidelityPass = replicaAtProduction.SequenceEqual(realProduction);
            _output.WriteLine($"FidelityPass(BitIdentical)={fidelityPass}, n={replicaAtProduction.Length}");
            if (!fidelityPass)
            {
                int mismatches = 0;
                for (int i = 0; i < replicaAtProduction.Length; i++)
                {
                    if (replicaAtProduction[i] != realProduction[i])
                    {
                        if (mismatches < 10)
                            _output.WriteLine($"  MISMATCH idx={i}: replica={replicaAtProduction[i]:G17}, real={realProduction[i]:G17}, raw={rawSequence[i]:G17}, diff={replicaAtProduction[i] - realProduction[i]:G17}");
                        mismatches++;
                    }
                }
                _output.WriteLine($"  TotalMismatches={mismatches}/{replicaAtProduction.Length}");
            }
            Assert.True(fidelityPass, "PersistenceHysteresisReplica @ production Alpha/Threshold must reproduce the REAL FusionStateManager's Persistence stable sequence bit-for-bit on real data.");

            // ══════════════════════ §22 RUN ISOLATION (A=0.01,B=0.03,C=0.075,B=0.03) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== RUN ISOLATION (A=0.01, B=0.03, C=0.075, B=0.03) ===");
            RunReplica(rawSequence, 0.01);
            double[] b1 = RunReplica(rawSequence, 0.03);
            RunReplica(rawSequence, 0.075);
            double[] b2 = RunReplica(rawSequence, 0.03);
            bool isolationPass = b1.SequenceEqual(b2);
            _output.WriteLine($"RunIsolationPass={isolationPass}");
            Assert.True(isolationPass, "Result(B1) must equal Result(B2) - no experiment configuration may contaminate another.");

            // ══════════════════════ §23 FINGERPRINT ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== EXPERIMENT FINGERPRINT (dataset+threshold+alpha => fingerprint) ===");
            foreach (double t in new[] { 0.01, 0.03, 0.075, 0.03 })
                _output.WriteLine($"threshold={t:F3} => ExperimentFingerprint={ExperimentFingerprint(fingerprint, t, ProductionAlpha)}");
            Assert.Equal(ExperimentFingerprint(fingerprint, 0.03, ProductionAlpha), ExperimentFingerprint(fingerprint, 0.03, ProductionAlpha));
            Assert.NotEqual(ExperimentFingerprint(fingerprint, 0.01, ProductionAlpha), ExperimentFingerprint(fingerprint, 0.03, ProductionAlpha));

            // ══════════════════════ §5/§7/§8/§9 GRID SWEEP - GLOBAL METRICS ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== RAW PERSISTENCE (threshold-independent, global) ===");
            PrintStats("PersistenceRaw", rawSequence);
            int rawChanges = CountChanges(rawSequence);
            _output.WriteLine($"RawChanges (bar-to-bar, bit-exact)={rawChanges} / {observations.Count - 1} ({100.0 * rawChanges / (observations.Count - 1):F1}%)");

            var gridResults = new List<GridResult>();
            _output.WriteLine("");
            _output.WriteLine("=== SENSITIVITY GRID (global) ===");
            _output.WriteLine("Threshold | UpdateCount | UpdateRate | SuppressionRate | Runs | MeanRun | MedianRun | MaxRun(bars) | MaxRun(hours) | StableMean | StableMedian | StableStdDev");
            foreach (double threshold in ThresholdGrid)
            {
                double[] stable = RunReplica(rawSequence, threshold);
                GridResult result = Analyze(threshold, rawSequence, stable, rawChanges);
                gridResults.Add(result);
                _output.WriteLine(
                    $"{threshold:F3} | {result.UpdateCount} | {result.UpdateRate:P2} | {result.SuppressionRate:P2} | {result.Runs.Count} | {result.Runs.Average(r => (double)r.Length):F2} | {Percentile(result.Runs.Select(r => (double)r.Length).OrderBy(v => v).ToList(), 0.5):F1} | {result.Runs.Max(r => r.Length)} | {result.Runs.Max(r => r.Length) * 5 / 60.0:F1}h | {result.StableMean:F4} | {result.StableMedian:F4} | {result.StableStdDev:F4}");
            }

            _output.WriteLine("");
            _output.WriteLine("=== FROZEN-RUN LENGTH PERCENTILES (global, per threshold) ===");
            foreach (GridResult result in gridResults)
            {
                List<double> lengths = result.Runs.Select(r => (double)r.Length).OrderBy(v => v).ToList();
                _output.WriteLine($"threshold={result.Threshold:F3}: P50={Percentile(lengths, 0.50):F1}, P75={Percentile(lengths, 0.75):F1}, P90={Percentile(lengths, 0.90):F1}, P95={Percentile(lengths, 0.95):F1}, P99={Percentile(lengths, 0.99):F1}");
            }

            // ══════════════════════ §10 TRANSITION / BREAKPOINT ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== GRID-TO-GRID DELTA (UpdateRate) - progressive vs breakpoint ===");
            for (int i = 1; i < gridResults.Count; i++)
            {
                GridResult prev = gridResults[i - 1], cur = gridResults[i];
                _output.WriteLine($"{prev.Threshold:F3}->{cur.Threshold:F3}: UpdateRate {prev.UpdateRate:P2} -> {cur.UpdateRate:P2} (delta={cur.UpdateRate - prev.UpdateRate:P2})");
            }

            // ══════════════════════ §11 LONGEST FROZEN RUN DETAIL (0.03 and neighbours + extremes) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== LONGEST FROZEN RUNS DETAIL (top 5, selected thresholds) ===");
            foreach (double t in new[] { 0.00, 0.02, 0.03, 0.04, 0.10 })
            {
                GridResult result = gridResults.First(r => r.Threshold == t);
                _output.WriteLine($"--- threshold={t:F3} ---");
                foreach (var run in result.Runs.OrderByDescending(r => r.Length).Take(5))
                    _output.WriteLine($"    Value={run.Value:F6}, Length={run.Length} bars (~{run.Length * 5 / 60.0:F1}h / {run.Length * 5 / 60.0 / 24.0:F2}d), {observations[run.StartIndex].Timestamp:yyyy-MM-dd HH:mm} .. {observations[run.EndIndex].Timestamp:yyyy-MM-dd HH:mm}");
            }

            // ══════════════════════ §12/§15 NORMALIZATION CONTROL ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== NORMALIZATION CONTROL: does removing hysteresis (threshold=0.00) restore Raw~=Stable? ===");
            GridResult zeroThreshold = gridResults.First(r => r.Threshold == 0.00);
            double[] rawSorted = rawSequence.OrderBy(v => v).ToArray();
            _output.WriteLine($"RawMedian={Percentile(rawSorted.ToList(), 0.5):F4}, RawMean={rawSequence.Average():F4}");
            _output.WriteLine($"StableMedian(threshold=0.00, i.e. EMA-only, alpha={ProductionAlpha:F2})={zeroThreshold.StableMedian:F4}, StableMean={zeroThreshold.StableMean:F4}");
            _output.WriteLine($"StableMedian(threshold=0.03, production)={gridResults.First(r => r.Threshold == 0.03).StableMedian:F4}");
            _output.WriteLine("Interpretation: gap between RawMedian and StableMedian at threshold=0.00 (hysteresis removed) isolates the EMA(alpha=0.20)'s OWN lag/compression contribution, independent of hysteresis.");

            // ══════════════════════ §16/§17 REGIME CONDITIONAL (UpdateRate/Raw/Stable only - frozen-run length NOT computed per-regime, see report §15) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== REGIME-CONDITIONAL UpdateRate (selected thresholds, descriptive - dataset is imbalanced) ===");
            foreach (var regimeGroup in observations.Select((o, i) => (o, i)).GroupBy(x => x.o.Winner).OrderByDescending(g => g.Count()))
            {
                int[] indices = regimeGroup.Select(x => x.i).ToArray();
                _output.WriteLine($"--- Winner={regimeGroup.Key} (n={indices.Length}, {100.0 * indices.Length / observations.Count:F1}% of dataset) ---");
                foreach (double t in new[] { 0.01, 0.03, 0.05, 0.10 })
                {
                    double[] stable = RunReplica(rawSequence, t);
                    (int updates, int total) = CountChangesAtIndices(stable, indices);
                    _output.WriteLine($"    threshold={t:F3}: UpdateRate(within-regime bars)={100.0 * updates / Math.Max(1, total):F1}% ({updates}/{total})");
                }
            }

            // ══════════════════════ §13/§18 BUY/SELL ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== BUY vs SELL (EntryTrigger direction, only bars where a candidate was produced) ===");
            int[] buyIndices = observations.Select((o, i) => (o, i)).Where(x => x.o.EntryDirection == DirectionCandidate.BUY_CANDIDATE).Select(x => x.i).ToArray();
            int[] sellIndices = observations.Select((o, i) => (o, i)).Where(x => x.o.EntryDirection == DirectionCandidate.SELL_CANDIDATE).Select(x => x.i).ToArray();
            _output.WriteLine($"BUY_CANDIDATE bars={buyIndices.Length} ({100.0 * buyIndices.Length / observations.Count:F2}%), SELL_CANDIDATE bars={sellIndices.Length} ({100.0 * sellIndices.Length / observations.Count:F2}%)");
            foreach (double t in new[] { 0.01, 0.03, 0.05 })
            {
                double[] stable = RunReplica(rawSequence, t);
                (int buyUpdates, int buyTotal) = CountChangesAtIndices(stable, buyIndices);
                (int sellUpdates, int sellTotal) = CountChangesAtIndices(stable, sellIndices);
                _output.WriteLine($"threshold={t:F3}: BUY UpdateRate={100.0 * buyUpdates / Math.Max(1, buyTotal):F1}% ({buyUpdates}/{buyTotal}), SELL UpdateRate={100.0 * sellUpdates / Math.Max(1, sellTotal):F1}% ({sellUpdates}/{sellTotal})");
                if (buyIndices.Length > 0) _output.WriteLine($"    BUY: RawMean={buyIndices.Average(i => rawSequence[i]):F4}, StableMean={buyIndices.Average(i => stable[i]):F4}");
                if (sellIndices.Length > 0) _output.WriteLine($"    SELL: RawMean={sellIndices.Average(i => rawSequence[i]):F4}, StableMean={sellIndices.Average(i => stable[i]):F4}");
            }

            // ══════════════════════ §19 DOWNSTREAM IMPACT (MeanRevertingRule/StableRangeRule only) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== DOWNSTREAM IMPACT: swap ONLY Persistence's stable value, hold every other real production dimension fixed ===");
            _output.WriteLine("Note: StableRangeRule does not read the Persistence dimension at all - only MeanRevertingRule's `NonPersistenceScientificWeight*(1-Persistence.Value)` term is sensitive to it.");
            double baselineCorrMrSr = CorrelationOfMrSr(observations, observations.Select(o => o.PersistenceStableProduction).ToArray());
            _output.WriteLine($"Baseline (production threshold=0.03): Corr(MR,SR)={baselineCorrMrSr:F4}");
            foreach (double t in ThresholdGrid)
            {
                if (t == ProductionThreshold) continue;
                double[] stable = RunReplica(rawSequence, t);
                double corr = CorrelationOfMrSr(observations, stable);
                int flips = CountMrSrOrderingFlips(observations, observations.Select(o => o.PersistenceStableProduction).ToArray(), stable);
                _output.WriteLine($"threshold={t:F3}: Corr(MR,SR)={corr:F4}, MR-vs-SR ordering flips vs production baseline={flips}/{observations.Count} ({100.0 * flips / observations.Count:F2}%)");
            }

            // ══════════════════════ §24 LOOK-AHEAD (real data, prefix/continuation) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== LOOK-AHEAD CHECK (real dataset, prefix vs full sequence) ===");
            int prefixLength = rawSequence.Length / 2;
            double[] prefixOnly = RunReplica(rawSequence.Take(prefixLength).ToArray(), ProductionThreshold);
            double[] fullSequence = RunReplica(rawSequence, ProductionThreshold);
            bool lookAheadPass = prefixOnly.SequenceEqual(fullSequence.Take(prefixLength));
            _output.WriteLine($"LookAheadPass(prefix unaffected by remainder)={lookAheadPass}, prefixLength={prefixLength}");
            Assert.True(lookAheadPass, "The first half of the stable sequence run alone must be bit-identical to the first half of the full-sequence run - no access to future bars.");

            _output.WriteLine("");
            _output.WriteLine("=== FINAL CLASSIFICATION INPUT (production threshold=0.03 row from grid above) ===");
            GridResult prod = gridResults.First(r => r.Threshold == ProductionThreshold);
            _output.WriteLine($"UpdateRate={prod.UpdateRate:P2}, SuppressionRate={prod.SuppressionRate:P2}, MaxRun={prod.Runs.Max(r => r.Length)} bars (~{prod.Runs.Max(r => r.Length) * 5 / 60.0:F1}h)");
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private sealed class FrozenRun
    {
        public double Value { get; init; }
        public int Length { get; init; }
        public int StartIndex { get; init; }
        public int EndIndex { get; init; }
    }

    private sealed class GridResult
    {
        public required double Threshold { get; init; }
        public required int UpdateCount { get; init; }
        public required double UpdateRate { get; init; }
        public required double SuppressionRate { get; init; }
        public required List<FrozenRun> Runs { get; init; }
        public required double StableMean { get; init; }
        public required double StableMedian { get; init; }
        public required double StableStdDev { get; init; }
    }

    private static double[] RunReplica(double[] rawSequence, double threshold)
    {
        var replica = new PersistenceHysteresisReplica(threshold, ProductionAlpha);
        var stable = new double[rawSequence.Length];
        for (int i = 0; i < rawSequence.Length; i++)
            stable[i] = replica.Update(rawSequence[i]);
        return stable;
    }

    private static GridResult Analyze(double threshold, double[] raw, double[] stable, int rawChanges)
    {
        int updateCount = CountChanges(stable);
        double updateRate = updateCount / (double)(stable.Length - 1);
        double suppressionRate = rawChanges > 0 ? 1.0 - updateCount / (double)rawChanges : double.NaN;
        List<FrozenRun> runs = ComputeRuns(stable);
        List<double> sortedStable = stable.OrderBy(v => v).ToList();

        return new GridResult
        {
            Threshold = threshold,
            UpdateCount = updateCount,
            UpdateRate = updateRate,
            SuppressionRate = suppressionRate,
            Runs = runs,
            StableMean = stable.Average(),
            StableMedian = Percentile(sortedStable, 0.5),
            StableStdDev = StdDev(stable)
        };
    }

    private static List<FrozenRun> ComputeRuns(double[] stable)
    {
        var runs = new List<FrozenRun>();
        int runStart = 0;
        for (int i = 1; i <= stable.Length; i++)
        {
            bool sameAsPrevious = i < stable.Length && stable[i] == stable[runStart];
            if (!sameAsPrevious)
            {
                runs.Add(new FrozenRun { Value = stable[runStart], Length = i - runStart, StartIndex = runStart, EndIndex = i - 1 });
                runStart = i;
            }
        }
        return runs;
    }

    private static int CountChanges(double[] series)
    {
        int count = 0;
        for (int i = 1; i < series.Length; i++)
            if (series[i] != series[i - 1]) count++;
        return count;
    }

    private static (int changes, int total) CountChangesAtIndices(double[] series, int[] indices)
    {
        int changes = 0, total = 0;
        foreach (int i in indices)
        {
            if (i == 0) continue; // no predecessor to compare against
            total++;
            if (series[i] != series[i - 1]) changes++;
        }
        return (changes, total);
    }

    private static double CorrelationOfMrSr(List<BarObservation> observations, double[] persistenceStable)
    {
        var mrRule = new MeanRevertingRule();
        var srRule = new StableRangeRule();
        var mr = new List<double>(observations.Count);
        var sr = new List<double>(observations.Count);
        for (int i = 0; i < observations.Count; i++)
        {
            FusionResult modified = WithPersistence(observations[i].ProductionStableResult, persistenceStable[i]);
            var mrBuilder = new DecisionResultBuilder();
            mrRule.Evaluate(new DecisionContext { FusionResult = modified, Evidence = null! }, mrBuilder);
            var srBuilder = new DecisionResultBuilder();
            srRule.Evaluate(new DecisionContext { FusionResult = modified, Evidence = null! }, srBuilder);
            mr.Add(mrBuilder.Confidence);
            sr.Add(srBuilder.Confidence);
        }
        return Correlation(mr, sr);
    }

    private static int CountMrSrOrderingFlips(List<BarObservation> observations, double[] baselinePersistence, double[] variantPersistence)
    {
        var mrRule = new MeanRevertingRule();
        var srRule = new StableRangeRule();
        int flips = 0;
        for (int i = 0; i < observations.Count; i++)
        {
            double baselineMr = ScoreMr(mrRule, observations[i].ProductionStableResult, baselinePersistence[i]);
            double baselineSr = ScoreSr(srRule, observations[i].ProductionStableResult, baselinePersistence[i]);
            double variantMr = ScoreMr(mrRule, observations[i].ProductionStableResult, variantPersistence[i]);
            double variantSr = ScoreSr(srRule, observations[i].ProductionStableResult, variantPersistence[i]);

            bool baselineMrWins = baselineMr > baselineSr;
            bool variantMrWins = variantMr > variantSr;
            if (baselineMrWins != variantMrWins) flips++;
        }
        return flips;
    }

    private static double ScoreMr(MeanRevertingRule rule, FusionResult baseResult, double persistenceValue)
    {
        var builder = new DecisionResultBuilder();
        rule.Evaluate(new DecisionContext { FusionResult = WithPersistence(baseResult, persistenceValue), Evidence = null! }, builder);
        return builder.Confidence;
    }

    private static double ScoreSr(StableRangeRule rule, FusionResult baseResult, double persistenceValue)
    {
        var builder = new DecisionResultBuilder();
        rule.Evaluate(new DecisionContext { FusionResult = WithPersistence(baseResult, persistenceValue), Evidence = null! }, builder);
        return builder.Confidence;
    }

    private static FusionResult WithPersistence(FusionResult source, double persistenceValue)
    {
        var builder = new FusionResultBuilder();
        foreach (var kv in source.Dimensions)
            builder.Dimensions[kv.Key] = kv.Value;
        FusionConfidence original = Dim(source, FusionDimension.Persistence);
        builder.Dimensions[FusionDimension.Persistence] = original with { Value = persistenceValue };
        return builder.Build();
    }

    private static string ExperimentFingerprint(string datasetFingerprint, double threshold, double alpha) =>
        $"{datasetFingerprint}|threshold={threshold:F6}|alpha={alpha:F6}";

    private static FusionConfidence Dim(FusionResult result, FusionDimension dimension) =>
        result.Dimensions.TryGetValue(dimension, out FusionConfidence? confidence)
            ? confidence
            : new FusionConfidence { Value = 0.0, Confidence = 0.0, Explanation = "Absent", IsAvailable = false };

    private void PrintStats(string label, IReadOnlyList<double> values)
    {
        List<double> sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) { _output.WriteLine($"{label}: n=0"); return; }
        _output.WriteLine(
            $"{label}: n={sorted.Count}, min={sorted[0]:F4}, max={sorted[^1]:F4}, mean={sorted.Average():F4}, stdDev={StdDev(values.ToArray()):F4}, " +
            $"P25={Percentile(sorted, 0.25):F4}, P50={Percentile(sorted, 0.50):F4}, P75={Percentile(sorted, 0.75):F4}");
    }

    private static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0.0;
        double mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return double.NaN;
        if (sorted.Count == 1) return sorted[0];
        double rank = p * (sorted.Count - 1);
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sorted[lower];
        double fraction = rank - lower;
        return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
    }

    private static double Correlation(IEnumerable<double> xs, IEnumerable<double> ys)
    {
        List<double> x = xs.ToList();
        List<double> y = ys.ToList();
        if (x.Count != y.Count || x.Count < 2) return double.NaN;
        double meanX = x.Average(), meanY = y.Average();
        double cov = 0.0, varX = 0.0, varY = 0.0;
        for (int i = 0; i < x.Count; i++)
        {
            double dx = x[i] - meanX, dy = y[i] - meanY;
            cov += dx * dy; varX += dx * dx; varY += dy * dy;
        }
        if (varX <= 0.0 || varY <= 0.0) return double.NaN;
        return cov / Math.Sqrt(varX * varY);
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException;
}
