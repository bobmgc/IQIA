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
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.Arbitration;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Risk;
using Xunit;
using Xunit.Abstractions;
using FusionEngine = IQIAIndicator.Engine.Fusion.EvidenceFusionEngine;
using FusionRandomWalkRule = IQIAIndicator.Engine.Fusion.Rules.RandomWalkRule;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 15.2, "Structural Break Evidence Integration Audit &amp; Correction"). AUDIT-ONLY,
/// OBSERVATION ONLY, DESCRIPTIVE ONLY - never touches production Fusion/FusionStateManager/DecisionEngine
/// (only ever CONSTRUCTS/CALLS them exactly as <see cref="BacktestEngine"/> or the Lot 15.0/14.17
/// precedent already does, mirroring their exact parallel-walk technique).
///
/// Central question this lot's brief asks to answer descriptively, and ONLY descriptively: "does
/// Cusum/BaiPerron evidence - which RegimeEngine.Collect already computes every bar but which NO
/// IFusionRule ever consumes (confirmed architectural fact from Lot 15.0/15.1) - contain information
/// related to the existing, production StructuralBreak classification?" This is NOT "what weight would
/// optimize anything" - no grid search, no threshold tuning, no weight search anywhere in this file. The
/// 0.5/0.5 hypothetical blend below is an ARBITRARY, illustrative choice for descriptive ablation only,
/// explicitly NOT a proposed production weighting - stated here and restated at its point of use.
///
/// Also re-measures StructuralBreak coverage (brief §10) over a fresh Yahoo pull, comparable to (not
/// expected to be identical to) Lot 15.0's regime_data_coverage.csv numbers (StructuralBreak: 3367 bars,
/// 30.802% of Ready bars, 262 runs, avg 12.85 bars/run, median 11, longest 79) - reused as this run's own
/// comparison baseline was already captured once and is restated in this file's report, not re-read from
/// that CSV at runtime (this run's own dataset pull is independently fresh; see Yahoo rolling-window
/// nondeterminism note in the project's own memory).
/// </summary>
public sealed class StructuralBreakEvidenceAblationLot152Tests
{
    private readonly ITestOutputHelper _output;

    public StructuralBreakEvidenceAblationLot152Tests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private sealed class BarObservation
    {
        public required int BarIndex;
        public required DateTime Timestamp;
        public required MarketState Winner;
        public required bool IsStructuralBreakWinner;
        public required double? StructuralBreakFinalScore;
        public required double StableStructuralStabilityValue;

        public required bool CusumValid;
        public required double? CusumConfidence;
        public required bool CusumChangeDetected;

        public required bool BaiPerronValid;
        public required double? BaiPerronConfidence;
        public required int? BaiPerronBreakCount;

        /// <summary>ARBITRARY, illustrative 0.5/0.5 blend of Cusum.Confidence and BaiPerron.Confidence -
        /// test-code-only, NEVER a proposed production weighting. Null when either input is unavailable
        /// (never fabricated as 0.0 - a missing evidence value is "unknown", not "measured zero").</summary>
        public double? HypotheticalScore => (CusumValid && BaiPerronValid)
            ? 0.5 * CusumConfidence!.Value + 0.5 * BaiPerronConfidence!.Value
            : null;
    }

    [Fact]
    public void Integration_Network_StructuralBreakEvidenceAblation_Lot152()
    {
        try
        {
            // ── Step 1: load the real dataset (same recipe as Lot 15.0/14.17 - can be the SAME run as the
            // coverage re-measurement, brief §10, no second download needed) ──────────────────────────────
            var source = new YahooHistoricalBarSource();
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-YahooHistoricalBarSource.DefaultMaxChunkSpanDays);

            HistoricalSeries series = source.Load("MES", "M5", from, to);
            Assert.True(series.Count > 0);

            string fingerprint = HistoricalSeriesFingerprint.Compute(series);
            const int warmupBars = 128;

            _output.WriteLine("=== DATASET IDENTITY (Lot 15.2, THIS run) ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");
            _output.WriteLine($"DatasetFingerprint={fingerprint}");

            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT15.2-FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            // ── Step 2: run the REAL, unmodified production pipeline exactly once ──────────────────────────
            BacktestSignalPipelineResult signalResult = new BacktestEngine().RunSignalPipeline(scenario, warmupBars);
            _output.WriteLine("");
            _output.WriteLine($"BarsProcessed={signalResult.BarsProcessed}, BarsRejected={signalResult.BarsRejected}, WarmupBars={signalResult.WarmupBars}, ReadyBars={signalResult.ReadyBars}, ExceptionCount={signalResult.ExceptionCount}, TotalSeriesBars={series.Count}");
            Assert.Equal(0, signalResult.ExceptionCount);

            // ── Step 3: parallel RAW->STABLE fusion walk (EXACT pattern of Lot 15.0/14.17 - own
            // EvidenceFusionEngine with the same 4 rules in the same order, own FusionStateManager, fed
            // bar-by-bar in chronological order from each bar's own .Regime EvidenceSet) - used ONLY to
            // obtain the STABLE StructuralStability dimension value for reporting; this file never
            // constructs a new FusionDimension and never wires Cusum/BaiPerron into it. ─────────────────────
            var fusionEngine = new FusionEngine(new IFusionRule[]
            {
                new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new FusionRandomWalkRule()
            });
            var fusionState = new FusionStateManager();

            var observations = new List<BarObservation>(series.Count);
            int cusumInvalidCount = 0, baiPerronInvalidCount = 0, missingStructuralBreakCandidateCount = 0;

            foreach (BacktestSignalResult bar in signalResult.Bars)
            {
                if (bar.Status is BacktestSignalStatus.Rejected or BacktestSignalStatus.Warmup or BacktestSignalStatus.Exception) continue;
                if (bar.Regime is null || bar.Decision is null) continue;

                FusionResult rawResult = fusionEngine.Fuse(new FusionContext
                {
                    Evidence = bar.Regime, Timestamp = bar.Timestamp, Symbol = series.Symbol, TimeFrame = series.TimeFrame, EvaluationId = Guid.Empty
                });
                FusionSnapshot snapshot = fusionState.Update(rawResult, bar.Timestamp);

                if (bar.Status != BacktestSignalStatus.Ready) continue;

                DecisionCandidate? sbCandidate = bar.Decision.Candidates.FirstOrDefault(c => c.MarketState == MarketState.StructuralBreak);
                if (sbCandidate is null) missingStructuralBreakCandidateCount++;

                EvidenceSet ev = bar.Regime;
                bool cusumValid = ev.Cusum?.IsValid ?? false;
                bool bpValid = ev.BaiPerron?.IsValid ?? false;
                if (!cusumValid) cusumInvalidCount++;
                if (!bpValid) baiPerronInvalidCount++;

                observations.Add(new BarObservation
                {
                    BarIndex = bar.BarIndex,
                    Timestamp = bar.Timestamp,
                    Winner = bar.Decision.Winner,
                    IsStructuralBreakWinner = bar.Decision.Winner == MarketState.StructuralBreak,
                    StructuralBreakFinalScore = sbCandidate?.FinalScore,
                    StableStructuralStabilityValue = Dim(snapshot.StableResult, FusionDimension.StructuralStability).Value,

                    CusumValid = cusumValid,
                    CusumConfidence = cusumValid ? ev.Cusum!.Confidence : null,
                    CusumChangeDetected = ev.Cusum?.ChangeDetected ?? false,

                    BaiPerronValid = bpValid,
                    BaiPerronConfidence = bpValid ? ev.BaiPerron!.Confidence : null,
                    BaiPerronBreakCount = bpValid ? ev.BaiPerron!.BreakCount : null
                });
            }

            _output.WriteLine("");
            _output.WriteLine($"ObservedReadyBars={observations.Count} (expected == ReadyBars {signalResult.ReadyBars})");
            Assert.Equal(signalResult.ReadyBars, observations.Count);
            _output.WriteLine($"CusumInvalidCount={cusumInvalidCount} ({Pct(cusumInvalidCount, observations.Count)}%), BaiPerronInvalidCount={baiPerronInvalidCount} ({Pct(baiPerronInvalidCount, observations.Count)}%), MissingStructuralBreakCandidateCount={missingStructuralBreakCandidateCount}");

            string outputDir = ResolveOutputDirectory();

            // ═══════════════════════════════ AGGREGATION + CSV + PRINT (run #1) ═══════════════════════════
            string hash1 = RunAggregationAndReport(observations, outputDir, print: true);

            // ═══════════════════════ Determinism/sanity re-run (no re-download) ════════════════════════════
            string hash2 = RunAggregationAndReport(observations, outputDir, print: false);
            _output.WriteLine("");
            _output.WriteLine($"=== DETERMINISM CHECK === Hash1={hash1}, Hash2={hash2}, Identical={hash1 == hash2}");
            Assert.Equal(hash1, hash2);
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            _output.WriteLine($"SKIPPED (network/Yahoo unavailable, not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ──────────────────────────────────────────── Aggregation + CSV ───────────────────────────────────────

    private string RunAggregationAndReport(List<BarObservation> observations, string outputDir, bool print)
    {
        var hashInput = new StringBuilder();
        var rows = new List<object?[]>();

        // ── Section CORRELATION: hypothetical 0.5/0.5 Cusum/BaiPerron blend vs the REAL StructuralBreak
        // decision output - PURELY DESCRIPTIVE. Not a search, not a proposed weighting. ────────────────────
        List<BarObservation> withHypothetical = observations.Where(o => o.HypotheticalScore.HasValue).ToList();
        List<BarObservation> withHypotheticalAndScore = withHypothetical.Where(o => o.StructuralBreakFinalScore.HasValue).ToList();

        double pearsonVsFinalScore = Correlation(
            withHypotheticalAndScore.Select(o => o.HypotheticalScore!.Value),
            withHypotheticalAndScore.Select(o => o.StructuralBreakFinalScore!.Value));
        double spearmanVsFinalScore = SpearmanCorrelation(
            withHypotheticalAndScore.Select(o => o.HypotheticalScore!.Value).ToList(),
            withHypotheticalAndScore.Select(o => o.StructuralBreakFinalScore!.Value).ToList());
        double pointBiserialVsWinner = Correlation(
            withHypothetical.Select(o => o.HypotheticalScore!.Value),
            withHypothetical.Select(o => o.IsStructuralBreakWinner ? 1.0 : 0.0));

        rows.Add(new object?[] { "CORRELATION", "Pearson(HypotheticalScore, StructuralBreakFinalScore)", withHypotheticalAndScore.Count, Math.Round(pearsonVsFinalScore, 6) });
        rows.Add(new object?[] { "CORRELATION", "Spearman(HypotheticalScore, StructuralBreakFinalScore)", withHypotheticalAndScore.Count, Math.Round(spearmanVsFinalScore, 6) });
        rows.Add(new object?[] { "CORRELATION", "PointBiserial(HypotheticalScore, IsStructuralBreakWinner)", withHypothetical.Count, Math.Round(pointBiserialVsWinner, 6) });
        rows.Add(new object?[] { "CORRELATION", "N_BarsWithBothCusumAndBaiPerronValid", withHypothetical.Count, Pct(withHypothetical.Count, observations.Count) });

        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== SECTION: CORRELATION (0.5/0.5 hypothetical blend is ARBITRARY/ILLUSTRATIVE, NOT a proposed production weight) ===");
            _output.WriteLine($"Pearson(HypotheticalScore, StructuralBreakFinalScore)      = {pearsonVsFinalScore:F6}  (n={withHypotheticalAndScore.Count})");
            _output.WriteLine($"Spearman(HypotheticalScore, StructuralBreakFinalScore)     = {spearmanVsFinalScore:F6}  (n={withHypotheticalAndScore.Count})");
            _output.WriteLine($"PointBiserial(HypotheticalScore, IsStructuralBreakWinner)  = {pointBiserialVsWinner:F6}  (n={withHypothetical.Count})");
        }

        // ── Section MEAN_BY_REGIME: hypothetical score + StructuralBreak FinalScore, per Winner regime ─────
        foreach (var g in observations.GroupBy(o => o.Winner).OrderByDescending(g => g.Count()))
        {
            List<BarObservation> bars = g.ToList();
            List<double> hyp = bars.Where(o => o.HypotheticalScore.HasValue).Select(o => o.HypotheticalScore!.Value).ToList();
            List<double> sbScore = bars.Where(o => o.StructuralBreakFinalScore.HasValue).Select(o => o.StructuralBreakFinalScore!.Value).ToList();
            rows.Add(new object?[]
            {
                "MEAN_BY_REGIME", g.Key, bars.Count,
                hyp.Count > 0 ? Math.Round(hyp.Average(), 6) : (double?)null,
                sbScore.Count > 0 ? Math.Round(sbScore.Average(), 6) : (double?)null
            });
        }
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== SECTION: MEAN_BY_REGIME (Regime, N, MeanHypotheticalScore, MeanStructuralBreakFinalScore) ===");
            foreach (var g in observations.GroupBy(o => o.Winner).OrderByDescending(g => g.Count()))
            {
                List<BarObservation> bars = g.ToList();
                List<double> hyp = bars.Where(o => o.HypotheticalScore.HasValue).Select(o => o.HypotheticalScore!.Value).ToList();
                List<double> sbScore = bars.Where(o => o.StructuralBreakFinalScore.HasValue).Select(o => o.StructuralBreakFinalScore!.Value).ToList();
                _output.WriteLine($"{g.Key}: n={bars.Count}, MeanHypothetical={(hyp.Count > 0 ? hyp.Average().ToString("F6") : "N/A")}, MeanStructuralBreakFinalScore={(sbScore.Count > 0 ? sbScore.Average().ToString("F6") : "N/A")}");
            }
        }

        // ── Section CONFIDENCE_STATS: per-regime descriptive stats of Cusum.Confidence / BaiPerron.Confidence
        // ([0,1] fields, NEW this lot - Lot 15.0's regime_evidence_stats.csv captured raw Cusum
        // magnitude/BaiPerron.BreakCount, not these Confidence fields specifically) ───────────────────────
        (string Name, Func<BarObservation, bool> Valid, Func<BarObservation, double?> Value)[] evidenceTypes =
        {
            ("Cusum.Confidence", o => o.CusumValid, o => o.CusumConfidence),
            ("BaiPerron.Confidence", o => o.BaiPerronValid, o => o.BaiPerronConfidence)
        };
        foreach (var g in observations.GroupBy(o => o.Winner).OrderByDescending(g => g.Count()))
        {
            List<BarObservation> bars = g.ToList();
            foreach (var (name, validFn, valueFn) in evidenceTypes)
            {
                List<double> validValues = bars.Where(validFn).Select(valueFn).Where(v => v.HasValue).Select(v => v!.Value).OrderBy(v => v).ToList();
                int validCount = bars.Count(validFn);
                rows.Add(new object?[]
                {
                    "CONFIDENCE_STATS", g.Key, name, validCount, bars.Count,
                    validValues.Count > 0 ? Math.Round(validValues.Average(), 6) : (double?)null,
                    validValues.Count > 0 ? Math.Round(StdDev(validValues), 6) : (double?)null,
                    validValues.Count > 0 ? Math.Round(validValues.Min(), 6) : (double?)null,
                    validValues.Count > 0 ? Math.Round(validValues.Max(), 6) : (double?)null,
                    validValues.Count > 0 ? Math.Round(Percentile(validValues, 0.5), 6) : (double?)null,
                    Pct(validCount, bars.Count)
                });
            }
        }
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== SECTION: CONFIDENCE_STATS (per regime, Cusum.Confidence / BaiPerron.Confidence descriptive stats) ===");
            foreach (object?[] r in rows.Where(r => (string)r[0]! == "CONFIDENCE_STATS"))
                _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Section COVERAGE: StructuralBreak coverage re-measurement (brief §10) ──────────────────────────
        int structuralBreakCount = observations.Count(o => o.IsStructuralBreakWinner);
        var runs = ComputeRuns(observations);
        int runCount = runs.Count;
        double avgRunBars = runCount > 0 ? runs.Average(r => r.Length) : 0.0;
        double medianRunBars = runCount > 0 ? Percentile(runs.Select(r => (double)r.Length).OrderBy(v => v).ToList(), 0.5) : 0.0;
        int longestRunBars = runCount > 0 ? runs.Max(r => r.Length) : 0;
        rows.Add(new object?[] { "COVERAGE", "StructuralBreak", structuralBreakCount, Pct(structuralBreakCount, observations.Count), runCount, Math.Round(avgRunBars, 2), Math.Round(medianRunBars, 2), longestRunBars });
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== SECTION: COVERAGE (StructuralBreak, THIS run vs Lot 15.0 baseline: 3367 bars/30.802%/262 runs/avg12.85/median11/longest79) ===");
            _output.WriteLine($"THIS RUN: BarCount={structuralBreakCount}, Pct={Pct(structuralBreakCount, observations.Count)}%, RunCount={runCount}, AvgRunBars={avgRunBars:F2}, MedianRunBars={medianRunBars:F2}, LongestRunBars={longestRunBars}");
        }

        WriteCsv(Path.Combine(outputDir, "structuralbreak_evidence_ablation_lot152.csv"),
            new[] { "Section", "Col1", "Col2", "Col3", "Col4", "Col5", "Col6", "Col7", "Col8", "Col9", "Col10" },
            rows);
        AppendHash(hashInput, rows);

        // ── Sanity: no NaN/Infinity in any numeric cell ─────────────────────────────────────────────────
        foreach (object?[] row in rows)
            foreach (object? cell in row)
                if (cell is double d) Assert.True(double.IsFinite(d), $"NaN/Infinity found in a report cell: {d}");

        return Sha256Hex(hashInput.ToString());
    }

    // ─────────────────────────────────────────────── Helpers ──────────────────────────────────────────────

    private static List<(int Length, TimeSpan Span)> ComputeRuns(List<BarObservation> observations)
    {
        var result = new List<(int, TimeSpan)>();
        if (observations.Count == 0) return result;

        int runStart = 0;
        for (int i = 1; i <= observations.Count; i++)
        {
            bool sameAsPrevious = i < observations.Count && observations[i].IsStructuralBreakWinner == observations[runStart].IsStructuralBreakWinner;
            if (!sameAsPrevious)
            {
                if (observations[runStart].IsStructuralBreakWinner)
                    result.Add((i - runStart, observations[i - 1].Timestamp - observations[runStart].Timestamp));
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

    /// <summary>Spearman = Pearson correlation of the RANKS (average rank on ties).</summary>
    private static double SpearmanCorrelation(List<double> x, List<double> y)
    {
        if (x.Count != y.Count || x.Count < 2) return double.NaN;
        return Correlation(Rank(x), Rank(y));
    }

    private static List<double> Rank(List<double> values)
    {
        int n = values.Count;
        var indexed = values.Select((v, i) => (v, i)).OrderBy(t => t.v).ToList();
        var ranks = new double[n];
        int idx = 0;
        while (idx < n)
        {
            int j = idx;
            while (j + 1 < n && indexed[j + 1].v == indexed[idx].v) j++;
            double avgRank = (idx + j) / 2.0 + 1.0; // 1-based average rank across the tie block
            for (int k = idx; k <= j; k++)
                ranks[indexed[k].i] = avgRank;
            idx = j + 1;
        }
        return ranks.ToList();
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
        {
            var padded = new object?[headers.Length];
            for (int i = 0; i < headers.Length; i++)
                padded[i] = i < row.Length ? row[i] : null;
            sb.AppendLine(string.Join(",", padded.Select(CsvCell)));
        }
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
