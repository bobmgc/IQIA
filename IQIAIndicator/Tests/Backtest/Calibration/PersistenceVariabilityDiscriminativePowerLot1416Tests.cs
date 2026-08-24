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
using Xunit;
using Xunit.Abstractions;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.16). INTEGRATION / NETWORK, OBSERVATION ONLY. Audits the historical variability and
/// discriminative power of the Fusion <c>Persistence</c> dimension (Lot 14.15's identified discriminant
/// between <see cref="MeanRevertingRule"/> and <see cref="StableRangeRule"/>) - never modifies its weight.
///
/// Same reuse discipline as Lot 14.14/14.15: <see cref="BacktestEngine.RunSignalPipeline"/> runs once
/// (production default), Fusion is independently replayed with the real
/// <see cref="EvidenceFusionEngine"/>/<see cref="FusionStateManager"/> classes, and the Lot 14.15 ablation
/// technique (real rule classes re-invoked on modified copies of real per-bar <see cref="FusionResult"/>) is
/// reproduced and extended (per-period, per-regime).
/// </summary>
public sealed class PersistenceVariabilityDiscriminativePowerLot1416Tests
{
    private readonly ITestOutputHelper _output;

    public PersistenceVariabilityDiscriminativePowerLot1416Tests(ITestOutputHelper output)
    {
        _output = output;
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
        public required MarketState? RunnerUp { get; init; }
        public required DirectionCandidate? Direction { get; init; }
        public required double MeanRevertingScore { get; init; }
        public required double StableRangeScore { get; init; }
        public required double AmbiguityScore { get; init; }
        public required FusionResult Fusion { get; init; }
        public required double Persistence { get; init; }
        public double ScoreDifference => MeanRevertingScore - StableRangeScore;
    }

    [Fact]
    public void Integration_Network_PersistenceVariabilityDiscriminativePower_Lot1416()
    {
        try
        {
            var source = new YahooHistoricalBarSource();
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-YahooHistoricalBarSource.DefaultMaxChunkSpanDays);

            HistoricalSeries series = source.Load("MES", "M5", from, to);
            Assert.True(series.Count > 0);

            string fingerprint = HistoricalSeriesFingerprint.Compute(series);
            const int warmupBars = 128;

            _output.WriteLine("=== DATASET IDENTITY (Lot 14.16, THIS run) ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");
            _output.WriteLine($"DatasetFingerprint={fingerprint}");

            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT14.16-FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
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

                FusionResult fusionResult = fusionEngine.Fuse(new FusionContext
                {
                    Evidence = bar.Regime, Timestamp = bar.Timestamp, Symbol = series.Symbol, TimeFrame = series.TimeFrame, EvaluationId = Guid.Empty
                });
                FusionSnapshot snapshot = fusionState.Update(fusionResult, bar.Timestamp);

                if (bar.Status != BacktestSignalStatus.Ready) continue;

                var ruleScores = bar.Decision.Candidates.ToDictionary(c => c.MarketState, c => c.FinalScore);
                MarketState? runnerUp = bar.Decision.Candidates.Length > 1 ? bar.Decision.Candidates[1].MarketState : null;

                observations.Add(new BarObservation
                {
                    BarIndex = bar.BarIndex,
                    Timestamp = bar.Timestamp,
                    Winner = bar.Decision.Winner,
                    RunnerUp = runnerUp,
                    Direction = bar.EntryTrigger?.Assessment.Direction,
                    MeanRevertingScore = ruleScores.GetValueOrDefault(MarketState.MeanReverting),
                    StableRangeScore = ruleScores.GetValueOrDefault(MarketState.StableRange),
                    AmbiguityScore = bar.Decision.AmbiguityScore,
                    Fusion = snapshot.StableResult,
                    Persistence = Dim(snapshot.StableResult, FusionDimension.Persistence).Value
                });
            }

            _output.WriteLine($"ObservedBars(Ready)={observations.Count}");
            List<double> persistence = observations.Select(o => o.Persistence).ToList();
            List<double> scoreDiff = observations.Select(o => o.ScoreDifference).ToList();

            // ══════════════════════ §6 GLOBAL DISTRIBUTION ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== GLOBAL PERSISTENCE DISTRIBUTION ===");
            PrintFullStats("Persistence(all)", persistence);

            // ══════════════════════ §7 TEMPORAL DISTRIBUTION (per calendar day - unbiased, natural boundary) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== TEMPORAL DISTRIBUTION (per calendar day, n>=50) ===");
            foreach (var dayGroup in observations.GroupBy(o => o.Timestamp.Date).OrderBy(g => g.Key))
            {
                List<BarObservation> dayBars = dayGroup.ToList();
                if (dayBars.Count < 50) continue;
                List<double> dayPersistence = dayBars.Select(o => o.Persistence).OrderBy(v => v).ToList();
                _output.WriteLine(
                    $"{dayGroup.Key:yyyy-MM-dd}: n={dayPersistence.Count}, mean={dayPersistence.Average():F4}, median={Percentile(dayPersistence, 0.50):F4}, " +
                    $"stdDev={StdDev(dayPersistence):F4}, P10={Percentile(dayPersistence, 0.10):F4}, P90={Percentile(dayPersistence, 0.90):F4}, min={dayPersistence[0]:F4}, max={dayPersistence[^1]:F4}");
            }

            // ══════════════════════ §8 REGIME DISTRIBUTION ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== PERSISTENCE BY REGIME (Winner) ===");
            foreach (var g in observations.GroupBy(o => o.Winner).OrderByDescending(g => g.Count()))
                PrintFullStats($"Persistence | Winner={g.Key} (n={g.Count()})", g.Select(o => o.Persistence));

            // ══════════════════════ §9 BUY/SELL DISTRIBUTION ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== PERSISTENCE BY DIRECTION (MeanReverting-winner bars only) ===");
            List<BarObservation> mrWins = observations.Where(o => o.Winner == MarketState.MeanReverting).ToList();
            PrintFullStats("Persistence | Direction=BUY_CANDIDATE", mrWins.Where(o => o.Direction == DirectionCandidate.BUY_CANDIDATE).Select(o => o.Persistence));
            PrintFullStats("Persistence | Direction=SELL_CANDIDATE", mrWins.Where(o => o.Direction == DirectionCandidate.SELL_CANDIDATE).Select(o => o.Persistence));

            // ══════════════════════ §10-11 PERSISTENCE VS SCOREDIFFERENCE (Pearson+Spearman) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== PERSISTENCE vs SCOREDIFFERENCE ===");
            _output.WriteLine($"Pearson  Corr(Persistence, ScoreDifference) = {Correlation(persistence, scoreDiff):F4}");
            _output.WriteLine($"Spearman Corr(Persistence, ScoreDifference) = {SpearmanCorrelation(persistence, scoreDiff):F4}");
            _output.WriteLine($"Pearson  Corr(Persistence, AmbiguityScore) = {Correlation(persistence, observations.Select(o => o.AmbiguityScore)):F4}");

            // ══════════════════════ §7/§13 MONOTONICITY (descriptive) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== MONOTONICITY CHECK: mean ScoreDifference per Persistence decile ===");
            List<BarObservation> sortedByPersistence = observations.OrderBy(o => o.Persistence).ToList();
            int decileSize = sortedByPersistence.Count / 10;
            var decileMeans = new List<double>();
            for (int d = 0; d < 10; d++)
            {
                List<BarObservation> bin = sortedByPersistence.Skip(d * decileSize).Take(d == 9 ? sortedByPersistence.Count - d * decileSize : decileSize).ToList();
                decileMeans.Add(bin.Average(o => o.ScoreDifference));
            }
            bool monotoneNonIncreasing = decileMeans.Zip(decileMeans.Skip(1), (a, b) => a >= b - 1e-9).All(x => x);
            _output.WriteLine($"Decile mean ScoreDifference sequence: [{string.Join(", ", decileMeans.Select(v => v.ToString("F4")))}]");
            _output.WriteLine($"Monotone non-increasing across deciles: {monotoneNonIncreasing}");

            // ══════════════════════ §13 QUANTILE/BINNING ANALYSIS ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== DECILE BINNING (Persistence deciles: ScoreDifference, AmbiguityScore, MR/SR win rate) ===");
            for (int d = 0; d < 10; d++)
            {
                List<BarObservation> bin = sortedByPersistence.Skip(d * decileSize).Take(d == 9 ? sortedByPersistence.Count - d * decileSize : decileSize).ToList();
                double loP = bin.Min(o => o.Persistence), hiP = bin.Max(o => o.Persistence);
                double meanDiff = bin.Average(o => o.ScoreDifference);
                double medianDiff = Percentile(bin.Select(o => o.ScoreDifference).OrderBy(v => v).ToList(), 0.5);
                double meanAmbiguity = bin.Average(o => o.AmbiguityScore);
                double mrWinRate = bin.Count(o => o.Winner == MarketState.MeanReverting) / (double)bin.Count;
                double srWinRate = bin.Count(o => o.Winner == MarketState.StableRange) / (double)bin.Count;
                _output.WriteLine(
                    $"Decile {d} [{loP:F4},{hiP:F4}] n={bin.Count}: meanDiff={meanDiff:F4}, medianDiff={medianDiff:F4}, " +
                    $"meanAmbiguity={meanAmbiguity:F4}, MR winRate={mrWinRate:P1}, SR winRate={srWinRate:P1}");
            }

            // ══════════════════════ §14 PERSISTENCE vs WINNER (descriptive) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== PERSISTENCE vs WINNER (descriptive frequency, NOT predictive) ===");
            foreach (var g in observations.GroupBy(o => o.Winner).OrderByDescending(g => g.Count()))
                _output.WriteLine($"Winner={g.Key}: n={g.Count()}, mean Persistence={g.Average(o => o.Persistence):F4}");

            // ══════════════════════ §15/§16/§17 PERSISTENCE vs RUNNERUP + OVERLAP ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== PERSISTENCE: Winner=MeanReverting&RunnerUp=StableRange vs Winner=StableRange&RunnerUp=MeanReverting ===");
            List<double> pMrWinSrRunner = observations.Where(o => o.Winner == MarketState.MeanReverting && o.RunnerUp == MarketState.StableRange).Select(o => o.Persistence).ToList();
            List<double> pSrWinMrRunner = observations.Where(o => o.Winner == MarketState.StableRange && o.RunnerUp == MarketState.MeanReverting).Select(o => o.Persistence).ToList();
            PrintFullStats("Persistence | Winner=MR,RunnerUp=SR", pMrWinSrRunner);
            PrintFullStats("Persistence | Winner=SR,RunnerUp=MR (CAUTION small sample)", pSrWinMrRunner);
            double overlap = OverlapCoefficient(pMrWinSrRunner, pSrWinMrRunner);
            _output.WriteLine($"Overlap coefficient (histogram intersection, 20 bins over pooled range) = {overlap:F4} (0=fully separated, 1=identical distributions)");

            // ══════════════════════ §12 ABLATION REPRODUCTION ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== ABLATION REPRODUCTION (Lot 14.15 baseline: 0.9653 / shared-removed: 0.1033 / persistence-removed: 0.9913) ===");
            double meanStationarity = observations.Average(o => Dim(o.Fusion, FusionDimension.Stationarity).Value);
            double meanMeanReversion = observations.Average(o => Dim(o.Fusion, FusionDimension.MeanReversion).Value);
            double meanPersistence = observations.Average(o => o.Persistence);
            RunAblation("Baseline", observations, f => f);
            RunAblation("Shared dimensions removed (Stationarity+MeanReversion clamped to mean)", observations, f => Clamp(Clamp(f, FusionDimension.Stationarity, meanStationarity), FusionDimension.MeanReversion, meanMeanReversion));
            RunAblation("Persistence removed (clamped to mean)", observations, f => Clamp(f, FusionDimension.Persistence, meanPersistence));

            // ══════════════════════ §18 ABLATION BY PERIOD (5 chronological chunks, unbiased equal-count split) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== ABLATION BY PERIOD (5 equal-count chronological chunks) ===");
            int chunkSize = observations.Count / 5;
            for (int c = 0; c < 5; c++)
            {
                List<BarObservation> chunk = observations.Skip(c * chunkSize).Take(c == 4 ? observations.Count - c * chunkSize : chunkSize).ToList();
                DateTime chunkStart = chunk.First().Timestamp;
                DateTime chunkEnd = chunk.Last().Timestamp;
                double chunkMeanStat = chunk.Average(o => Dim(o.Fusion, FusionDimension.Stationarity).Value);
                double chunkMeanMR = chunk.Average(o => Dim(o.Fusion, FusionDimension.MeanReversion).Value);
                double chunkMeanPers = chunk.Average(o => o.Persistence);
                _output.WriteLine($"--- Period {c} [{chunkStart:yyyy-MM-dd}..{chunkEnd:yyyy-MM-dd}], n={chunk.Count} ---");
                RunAblation("  Baseline", chunk, f => f);
                RunAblation("  Persistence removed", chunk, f => Clamp(f, FusionDimension.Persistence, chunkMeanPers));
            }

            // ══════════════════════ §19 ABLATION BY REGIME (only regimes with sufficient sample) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== ABLATION BY REGIME (n>=300 only) ===");
            foreach (var g in observations.GroupBy(o => o.Winner))
            {
                List<BarObservation> regimeBars = g.ToList();
                if (regimeBars.Count < 300) { _output.WriteLine($"Winner={g.Key}: n={regimeBars.Count} - SKIPPED (insufficient sample, brief §14/§19)"); continue; }
                double regimeMeanPers = regimeBars.Average(o => o.Persistence);
                _output.WriteLine($"--- Winner={g.Key}, n={regimeBars.Count} ---");
                RunAblation("  Baseline", regimeBars, f => f);
                RunAblation("  Persistence removed", regimeBars, f => Clamp(f, FusionDimension.Persistence, regimeMeanPers));
            }

            // ══════════════════════ §21 TEMPORAL STABILITY OF Corr(Persistence, ScoreDifference) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== TEMPORAL STABILITY: Corr(Persistence, ScoreDifference) per day ===");
            var dailyCorr = new List<double>();
            foreach (var dayGroup in observations.GroupBy(o => o.Timestamp.Date).OrderBy(g => g.Key))
            {
                List<BarObservation> dayBars = dayGroup.ToList();
                if (dayBars.Count < 50) continue;
                double p = Correlation(dayBars.Select(o => o.Persistence), dayBars.Select(o => o.ScoreDifference));
                double s = SpearmanCorrelation(dayBars.Select(o => o.Persistence).ToList(), dayBars.Select(o => o.ScoreDifference).ToList());
                dailyCorr.Add(p);
                _output.WriteLine($"{dayGroup.Key:yyyy-MM-dd}: n={dayBars.Count}, Pearson={p:F4}, Spearman={s:F4}");
            }
            if (dailyCorr.Count > 0)
                _output.WriteLine($"Daily Pearson Corr(Persistence,ScoreDifference): min={dailyCorr.Min():F4}, max={dailyCorr.Max():F4}, mean={dailyCorr.Average():F4}, days={dailyCorr.Count}");

            // ══════════════════════ §22 REGIME STABILITY OF Corr(Persistence, ScoreDifference) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== REGIME STABILITY: Corr(Persistence, ScoreDifference) per Winner regime ===");
            foreach (var g in observations.GroupBy(o => o.Winner).OrderByDescending(g => g.Count()))
            {
                List<BarObservation> regimeBars = g.ToList();
                string caution = regimeBars.Count < 300 ? " (CAUTION small sample)" : "";
                double p = Correlation(regimeBars.Select(o => o.Persistence), regimeBars.Select(o => o.ScoreDifference));
                double s = SpearmanCorrelation(regimeBars.Select(o => o.Persistence).ToList(), regimeBars.Select(o => o.ScoreDifference).ToList());
                _output.WriteLine($"Winner={g.Key}, n={regimeBars.Count}: Pearson={p:F4}, Spearman={s:F4}{caution}");
            }
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            _output.WriteLine($"SKIPPED (network/Yahoo unavailable, not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void RunAblation(string label, List<BarObservation> observations, Func<FusionResult, FusionResult> transform)
    {
        var mrRule = new MeanRevertingRule();
        var srRule = new StableRangeRule();

        var mr = new List<double>(observations.Count);
        var sr = new List<double>(observations.Count);

        foreach (BarObservation o in observations)
        {
            FusionResult modified = transform(o.Fusion);
            var mrBuilder = new DecisionResultBuilder();
            mrRule.Evaluate(new DecisionContext { FusionResult = modified, Evidence = null! }, mrBuilder);
            var srBuilder = new DecisionResultBuilder();
            srRule.Evaluate(new DecisionContext { FusionResult = modified, Evidence = null! }, srBuilder);
            mr.Add(mrBuilder.Confidence);
            sr.Add(srBuilder.Confidence);
        }

        double corr = Correlation(mr, sr);
        _output.WriteLine($"{label}: n={observations.Count}, Corr(MR,SR)={corr:F4}");
    }

    private static FusionResult Clamp(FusionResult source, FusionDimension dimension, double value)
    {
        var builder = new FusionResultBuilder();
        foreach (var kv in source.Dimensions)
            builder.Dimensions[kv.Key] = kv.Value;
        FusionConfidence original = Dim(source, dimension);
        builder.Dimensions[dimension] = original with { Value = value };
        return builder.Build();
    }

    private static FusionConfidence Dim(FusionResult result, FusionDimension dimension) =>
        result.Dimensions.TryGetValue(dimension, out FusionConfidence? confidence)
            ? confidence
            : new FusionConfidence { Value = 0.0, Confidence = 0.0, Explanation = "Absent", IsAvailable = false };

    private void PrintFullStats(string label, IEnumerable<double> values)
    {
        List<double> sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) { _output.WriteLine($"{label}: n=0 (no data)"); return; }

        double mean = sorted.Average();
        _output.WriteLine(
            $"{label}: n={sorted.Count}, min={sorted[0]:F4}, max={sorted[^1]:F4}, mean={mean:F4}, stdDev={StdDev(sorted):F4}, " +
            $"P01={Percentile(sorted, 0.01):F4}, P05={Percentile(sorted, 0.05):F4}, P10={Percentile(sorted, 0.10):F4}, P25={Percentile(sorted, 0.25):F4}, " +
            $"P50={Percentile(sorted, 0.50):F4}, P75={Percentile(sorted, 0.75):F4}, P90={Percentile(sorted, 0.90):F4}, P95={Percentile(sorted, 0.95):F4}, P99={Percentile(sorted, 0.99):F4}");
    }

    private static double OverlapCoefficient(List<double> a, List<double> b)
    {
        if (a.Count == 0 || b.Count == 0) return double.NaN;
        double lo = Math.Min(a.Min(), b.Min());
        double hi = Math.Max(a.Max(), b.Max());
        if (hi <= lo) return 1.0;
        const int bins = 20;
        double width = (hi - lo) / bins;
        var histA = new double[bins];
        var histB = new double[bins];
        foreach (double v in a) histA[Math.Min(bins - 1, (int)((v - lo) / width))]++;
        foreach (double v in b) histB[Math.Min(bins - 1, (int)((v - lo) / width))]++;
        for (int i = 0; i < bins; i++) { histA[i] /= a.Count; histB[i] /= b.Count; }
        return Enumerable.Range(0, bins).Sum(i => Math.Min(histA[i], histB[i]));
    }

    private static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0.0;
        double mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }

    private static double Percentile(List<double> sorted, double p)
    {
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

        double meanX = x.Average();
        double meanY = y.Average();
        double cov = 0.0, varX = 0.0, varY = 0.0;
        for (int i = 0; i < x.Count; i++)
        {
            double dx = x[i] - meanX;
            double dy = y[i] - meanY;
            cov += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }

        if (varX <= 0.0 || varY <= 0.0) return double.NaN;
        return cov / Math.Sqrt(varX * varY);
    }

    private static double SpearmanCorrelation(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        double[] rankX = Rank(x);
        double[] rankY = Rank(y);
        return Correlation(rankX, rankY);
    }

    private static double[] Rank(IReadOnlyList<double> values)
    {
        int[] indices = Enumerable.Range(0, values.Count).OrderBy(i => values[i]).ToArray();
        double[] ranks = new double[values.Count];
        int i2 = 0;
        while (i2 < indices.Length)
        {
            int j = i2;
            while (j + 1 < indices.Length && values[indices[j + 1]] == values[indices[i2]]) j++;
            double avgRank = (i2 + j) / 2.0 + 1.0;
            for (int k = i2; k <= j; k++) ranks[indices[k]] = avgRank;
            i2 = j + 1;
        }
        return ranks;
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or InvalidOperationException;
}
