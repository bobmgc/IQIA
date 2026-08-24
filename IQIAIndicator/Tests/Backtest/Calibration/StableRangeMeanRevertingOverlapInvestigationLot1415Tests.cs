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
using IQIAIndicator.Engine.Fusion;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Risk;
using Xunit;
using Xunit.Abstractions;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.15). INTEGRATION / NETWORK, OBSERVATION ONLY. Investigates whether
/// <see cref="Engine.Decision.Rules.StableRangeRule"/> and <see cref="Engine.Decision.Rules.MeanRevertingRule"/>
/// are two genuinely distinct market hypotheses or largely the same phenomenon under two names - the
/// question raised by Lot 14.14's finding that their FinalScore correlates at 0.9652 via shared
/// Stationarity+MeanReversion weight.
///
/// Same reuse discipline as Lot 14.14: <see cref="BacktestEngine.RunSignalPipeline"/> runs EXACTLY ONCE
/// (production default, unmodified), and Fusion is independently replayed with the SAME real
/// <see cref="EvidenceFusionEngine"/>/<see cref="FusionStateManager"/> classes fed the already-computed
/// <c>EvidenceSet</c> per bar - never a reimplementation of any formula. The ablation analysis (brief §13)
/// re-invokes the REAL <see cref="MeanRevertingRule"/>/<see cref="StableRangeRule"/> classes directly on
/// COPIES of the real per-bar <see cref="FusionResult"/> with one dimension clamped to the dataset's own
/// mean - never a hand-derived formula, never a change to any production file.
/// </summary>
public sealed class StableRangeMeanRevertingOverlapInvestigationLot1415Tests
{
    private readonly ITestOutputHelper _output;

    public StableRangeMeanRevertingOverlapInvestigationLot1415Tests(ITestOutputHelper output)
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
        public required IReadOnlyDictionary<MarketState, double> RuleScores { get; init; }
        public required FusionResult Fusion { get; init; }
        public required double Stationarity { get; init; }
        public required double Persistence { get; init; }
        public required double MeanReversion { get; init; }
        public required double RandomWalk { get; init; }
        public required double StructuralStability { get; init; }
    }

    [Fact]
    public void Integration_Network_StableRangeMeanRevertingOverlap_Lot1415()
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

            _output.WriteLine("=== DATASET IDENTITY (Lot 14.15, THIS run) ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");
            _output.WriteLine($"DatasetFingerprint={fingerprint}");

            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT14.15-FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
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
                    RuleScores = ruleScores,
                    Fusion = snapshot.StableResult,
                    Stationarity = Dim(snapshot.StableResult, FusionDimension.Stationarity).Value,
                    Persistence = Dim(snapshot.StableResult, FusionDimension.Persistence).Value,
                    MeanReversion = Dim(snapshot.StableResult, FusionDimension.MeanReversion).Value,
                    RandomWalk = Dim(snapshot.StableResult, FusionDimension.RandomWalk).Value,
                    StructuralStability = Dim(snapshot.StableResult, FusionDimension.StructuralStability).Value
                });
            }

            _output.WriteLine($"ObservedBars(Ready)={observations.Count}");
            Assert.True(observations.All(o => o.RuleScores.ContainsKey(MarketState.MeanReverting) && o.RuleScores.ContainsKey(MarketState.StableRange)));

            List<double> mrScores = observations.Select(o => o.RuleScores[MarketState.MeanReverting]).ToList();
            List<double> srScores = observations.Select(o => o.RuleScores[MarketState.StableRange]).ToList();
            List<double> scoreDiff = observations.Select(o => o.RuleScores[MarketState.MeanReverting] - o.RuleScores[MarketState.StableRange]).ToList();

            // ══════════════════════ §5/§9 DIMENSION-TO-DIMENSION CORRELATIONS ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== DIMENSION-TO-DIMENSION CORRELATIONS (Fusion Value, no 'Volatility' dimension exists - FusionDimension has exactly 5 values) ===");
            var dims = new (string Name, Func<BarObservation, double> Selector)[]
            {
                ("Stationarity", o => o.Stationarity), ("Persistence", o => o.Persistence), ("MeanReversion", o => o.MeanReversion),
                ("StructuralStability", o => o.StructuralStability), ("RandomWalk", o => o.RandomWalk)
            };
            for (int i = 0; i < dims.Length; i++)
                for (int j = i + 1; j < dims.Length; j++)
                    _output.WriteLine($"Corr({dims[i].Name}, {dims[j].Name}) = {Correlation(observations.Select(dims[i].Selector), observations.Select(dims[j].Selector)):F4}");

            // ══════════════════════ §6/§10 RULE-TO-RULE CORRELATION REPRODUCTION ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== RULE CORRELATION REPRODUCTION (Lot 14.14 measured 0.9652 on its own dataset) ===");
            double pearson = Correlation(mrScores, srScores);
            double spearman = SpearmanCorrelation(mrScores, srScores);
            _output.WriteLine($"Corr(MeanRevertingScore, StableRangeScore) Pearson = {pearson:F4}");
            _output.WriteLine($"Corr(MeanRevertingScore, StableRangeScore) Spearman = {spearman:F4}");

            // ══════════════════════ §7 SCORE DIFFERENCE ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== SCORE DIFFERENCE (MeanRevertingScore - StableRangeScore) ===");
            PrintStats("ScoreDifference(MR-SR)", scoreDiff);
            foreach (double threshold in new[] { 0.01, 0.02, 0.05, 0.10 })
            {
                int within = scoreDiff.Count(d => Math.Abs(d) < threshold);
                _output.WriteLine($"|MR-SR| < {threshold:F2}: {within}/{scoreDiff.Count} ({100.0 * within / scoreDiff.Count:F2}%)");
            }
            _output.WriteLine("Histogram (MR-SR), bucket width 0.05:");
            foreach (var bucket in scoreDiff.GroupBy(d => Math.Floor(d / 0.05) * 0.05).OrderBy(g => g.Key))
                _output.WriteLine($"  [{bucket.Key:F2}, {bucket.Key + 0.05:F2}): {bucket.Count()}");

            // ══════════════════════ §8/§12 WINNER/RUNNER-UP FREQUENCY ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== WINNER FREQUENCY ===");
            foreach (var g in observations.GroupBy(o => o.Winner).OrderByDescending(g => g.Count()))
                _output.WriteLine($"Winner={g.Key}: {g.Count()} ({100.0 * g.Count() / observations.Count:F1}%)");

            _output.WriteLine("");
            _output.WriteLine("=== RUNNERUP FREQUENCY WHEN WINNER=MeanReverting ===");
            List<BarObservation> mrWins = observations.Where(o => o.Winner == MarketState.MeanReverting).ToList();
            foreach (var g in mrWins.GroupBy(o => o.RunnerUp).OrderByDescending(g => g.Count()))
                _output.WriteLine($"RunnerUp={g.Key}: {g.Count()} ({100.0 * g.Count() / mrWins.Count:F1}% of MeanReverting wins)");

            _output.WriteLine("");
            _output.WriteLine("=== RUNNERUP FREQUENCY WHEN WINNER=StableRange ===");
            List<BarObservation> srWins = observations.Where(o => o.Winner == MarketState.StableRange).ToList();
            if (srWins.Count > 0)
                foreach (var g in srWins.GroupBy(o => o.RunnerUp).OrderByDescending(g => g.Count()))
                    _output.WriteLine($"RunnerUp={g.Key}: {g.Count()} ({100.0 * g.Count() / srWins.Count:F1}% of StableRange wins, n={srWins.Count} - CAUTION small sample)");
            else
                _output.WriteLine("No StableRange wins observed on this dataset.");

            // ══════════════════════ §13 REGIME-CONDITIONAL SCORE DIFFERENCE ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== REGIME-CONDITIONAL SCORE DIFFERENCE (MR-SR), grouped by Winner ===");
            foreach (var g in observations.GroupBy(o => o.Winner).OrderByDescending(g => g.Count()))
                PrintStats($"ScoreDifference(MR-SR) | Winner={g.Key} (n={g.Count()})", g.Select(o => o.RuleScores[MarketState.MeanReverting] - o.RuleScores[MarketState.StableRange]));

            // ══════════════════════ §14 TEMPORAL ANALYSIS (per-day rolling correlation) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== TEMPORAL ANALYSIS: PER-CALENDAR-DAY Corr(MeanRevertingScore, StableRangeScore) ===");
            var dailyCorrelations = new List<(DateTime Day, double Corr, int N)>();
            foreach (var dayGroup in observations.GroupBy(o => o.Timestamp.Date).OrderBy(g => g.Key))
            {
                List<BarObservation> dayBars = dayGroup.ToList();
                if (dayBars.Count < 50) continue; // brief §16: don't draw conclusions from small samples
                double dayCorr = Correlation(
                    dayBars.Select(o => o.RuleScores[MarketState.MeanReverting]),
                    dayBars.Select(o => o.RuleScores[MarketState.StableRange]));
                dailyCorrelations.Add((dayGroup.Key, dayCorr, dayBars.Count));
                _output.WriteLine($"{dayGroup.Key:yyyy-MM-dd}: n={dayBars.Count}, Corr={dayCorr:F4}");
            }
            if (dailyCorrelations.Count > 0)
            {
                _output.WriteLine($"Daily correlation: min={dailyCorrelations.Min(d => d.Corr):F4}, max={dailyCorrelations.Max(d => d.Corr):F4}, mean={dailyCorrelations.Average(d => d.Corr):F4}, days={dailyCorrelations.Count}");
            }

            // ══════════════════════ §17/§18 PERSISTENCE AS DISCRIMINANT ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== PERSISTENCE ANALYSIS ===");
            _output.WriteLine($"Corr(Persistence, MeanRevertingScore) = {Correlation(observations.Select(o => o.Persistence), mrScores):F4}");
            _output.WriteLine($"Corr(Persistence, StableRangeScore)   = {Correlation(observations.Select(o => o.Persistence), srScores):F4}");
            _output.WriteLine($"Corr(Persistence, ScoreDifference[MR-SR]) = {Correlation(observations.Select(o => o.Persistence), scoreDiff):F4}");
            List<double> persistenceValues = observations.Select(o => o.Persistence).OrderBy(v => v).ToList();
            double persistenceMedian = Percentile(persistenceValues, 0.50);
            List<double> lowPersistenceDiff = observations.Where(o => o.Persistence < persistenceMedian).Select(o => o.RuleScores[MarketState.MeanReverting] - o.RuleScores[MarketState.StableRange]).ToList();
            List<double> highPersistenceDiff = observations.Where(o => o.Persistence >= persistenceMedian).Select(o => o.RuleScores[MarketState.MeanReverting] - o.RuleScores[MarketState.StableRange]).ToList();
            _output.WriteLine($"|ScoreDifference| mean, Persistence BELOW median ({persistenceMedian:F4}): {lowPersistenceDiff.Select(Math.Abs).Average():F4} (n={lowPersistenceDiff.Count})");
            _output.WriteLine($"|ScoreDifference| mean, Persistence AT/ABOVE median: {highPersistenceDiff.Select(Math.Abs).Average():F4} (n={highPersistenceDiff.Count})");

            // ══════════════════════ §19 STRUCTURALSTABILITY ANALYSIS (shared, not discriminant) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== STRUCTURALSTABILITY ANALYSIS ===");
            _output.WriteLine($"Corr(StructuralStability, MeanRevertingScore) = {Correlation(observations.Select(o => o.StructuralStability), mrScores):F4}");
            _output.WriteLine($"Corr(StructuralStability, StableRangeScore)   = {Correlation(observations.Select(o => o.StructuralStability), srScores):F4}");
            _output.WriteLine($"Corr(StructuralStability, ScoreDifference[MR-SR]) = {Correlation(observations.Select(o => o.StructuralStability), scoreDiff):F4}");

            // ══════════════════════ §13 ABLATION (real rule classes, real per-bar Fusion, one dimension clamped to dataset mean) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== ABLATION ANALYSIS (real MeanRevertingRule/StableRangeRule re-invoked on modified real FusionResult copies) ===");
            double meanStationarity = observations.Average(o => o.Stationarity);
            double meanMeanReversion = observations.Average(o => o.MeanReversion);
            double meanPersistence = observations.Average(o => o.Persistence);
            double meanStructuralStability = observations.Average(o => o.StructuralStability);

            RunAblation("Baseline (no ablation)", observations, f => f);
            RunAblation("Ablate Persistence (clamp to dataset mean, discriminant dimension)", observations, f => Clamp(f, FusionDimension.Persistence, meanPersistence));
            RunAblation("Ablate SHARED dimensions (Stationarity+MeanReversion clamped to dataset mean)", observations, f => Clamp(Clamp(f, FusionDimension.Stationarity, meanStationarity), FusionDimension.MeanReversion, meanMeanReversion));
            RunAblation("Ablate StructuralStability (clamp to dataset mean)", observations, f => Clamp(f, FusionDimension.StructuralStability, meanStructuralStability));
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
        List<double> diff = mr.Zip(sr, (a, b) => a - b).ToList();
        double meanAbsDiff = diff.Select(Math.Abs).Average();
        _output.WriteLine($"{label}: Corr(MR,SR)={corr:F4}, mean|MR-SR|={meanAbsDiff:F4}, stdDev(MR)={StdDev(mr):F4}, stdDev(SR)={StdDev(sr):F4}");
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

    private void PrintStats(string label, IEnumerable<double> values)
    {
        List<double> sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) { _output.WriteLine($"{label}: n=0 (no data)"); return; }

        double mean = sorted.Average();
        double stdDev = StdDev(sorted);
        _output.WriteLine(
            $"{label}: n={sorted.Count}, min={sorted[0]:F4}, max={sorted[^1]:F4}, mean={mean:F4}, stdDev={stdDev:F4}, " +
            $"P05={Percentile(sorted, 0.05):F4}, P25={Percentile(sorted, 0.25):F4}, P50={Percentile(sorted, 0.50):F4}, " +
            $"P75={Percentile(sorted, 0.75):F4}, P95={Percentile(sorted, 0.95):F4}, P99={Percentile(sorted, 0.99):F4}");
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
