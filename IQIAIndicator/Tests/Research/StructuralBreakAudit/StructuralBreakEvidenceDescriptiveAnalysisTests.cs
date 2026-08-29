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
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.BaiPerron;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;
using IQIAIndicator.Engine.Risk;
using Xunit;
using IQIAIndicator.Tests.BacktestTests.Yahoo;

namespace IQIAIndicator.Tests.Research.StructuralBreakAudit;

/// <summary>
/// Sprint 15.6 ("Structural Break Evidence - Scientific Contract &amp; Integration Design Audit"),
/// AUDIT-ONLY, DESCRIPTIVE-ONLY. Never touches production code - only constructs/calls
/// <see cref="BacktestEngine"/> exactly as every prior Lot 15.x/14.x analysis test already does, and reads
/// the resulting <see cref="EvidenceSet"/> fields per bar (<c>.Cusum</c>, <c>.BaiPerron</c>) and the
/// <see cref="Engine.Decision.Core.DecisionResult.Winner"/> per bar. No calibration, no threshold
/// selection, no optimization, no "best" anything is performed anywhere in this file - every number
/// produced here is a plain descriptive statistic (count, mean, percentile, run length, Pearson
/// correlation) over the real Yahoo MES M5 dataset.
///
/// Central operational definitions used throughout this file (stated once here, and restated at the
/// point of use in code comments and in the printed report, because <see cref="CusumResult"/> has no
/// boolean detection field of its own to mirror and the absolute-bar-index translation is not otherwise
/// documented anywhere):
///
/// 1. "Bai-Perron detected a break on this bar" := <c>BaiPerron.BreakCount &gt; 0</c>. Bai-Perron has no
///    boolean analogous to Cusum's <c>ChangeDetected</c>, so this is an explicit, arbitrary-but-stated
///    choice for this audit only - it is NOT a claim that this is the "correct" contract definition.
///
/// 2. Absolute bar index translation, CUSUM: <c>cusumAbsoluteBreakBar = currentBarIndex - (SampleSize - 1
///    - EstimatedBreakIndex)</c>, computed only when <c>ChangeDetected &amp;&amp; IsValid &amp;&amp; 0
///    &lt;= EstimatedBreakIndex &lt; SampleSize</c>. <see cref="CusumStatistics"/> (read, not modified) can
///    legitimately emit <c>EstimatedBreakIndex == SampleSize</c> (its "candidate reset index" bookkeeping
///    uses <c>i + 1</c>, which can reach exactly the array length) - plugging that value into the formula
///    above would yield <c>currentBarIndex + 1</c>, a future bar, which is not a meaningful absolute
///    location for the comparisons below. Bars where this occurs are counted separately as
///    <c>CusumIndexAnomaly</c> and excluded from the absolute-index-based sections (disagreement timing);
///    they remain fully included in the overlap/agreement and clustering sections, which never use the
///    absolute index.
///
/// 3. Absolute bar index translation, Bai-Perron: same formula, applied to
///    <c>Breakpoints.Max()</c> ("the most recent breakpoint" - breakpoints are local window indices in
///    ascending chronological order per <see cref="BaiPerronStatistics.ReconstructBreakpoints"/>, read not
///    modified, so the largest local index is the most recent one), computed only when <c>BreakCount &gt;
///    0 &amp;&amp; IsValid &amp;&amp; 0 &lt;= maxLocalIndex &lt; SampleSize</c> (this bound is never violated
///    in the current implementation - Bai-Perron breakpoints are DP segment-start indices strictly less
///    than SampleSize - but the guard is kept explicit and counted as <c>BaiPerronIndexAnomaly</c> so a
///    silent implementation change would show up here rather than corrupting the arithmetic).
///
/// 4. <c>BreakAgeBars = currentBarIndex - mostRecentAbsoluteBreakpointBar</c> (Bai-Perron only, per the
///    brief; algebraically equals <c>SampleSize - 1 - maxLocalIndex</c>, but computed via the absolute
///    index for traceability against definition 3 above). 0 = break exactly at the window's trailing
///    edge; up to <c>SampleSize - 1</c> (~127 for the 128-bar Bai-Perron window) = break at the very start
///    of the window.
/// </summary>
public sealed class StructuralBreakEvidenceDescriptiveAnalysisTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public StructuralBreakEvidenceDescriptiveAnalysisTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    /// <summary>All 7 <see cref="MarketState"/> values, in declaration order - used for every
    /// regime-conditional section so absent regimes (expected: Transitional, Unknown - brief §21) still
    /// print an explicit N=0 row instead of silently vanishing from the report.</summary>
    private static readonly MarketState[] AllRegimes = Enum.GetValues<MarketState>();

    private sealed class BarObservation
    {
        public required int BarIndex;
        public required MarketState Winner;

        public required bool CusumIsValid;
        public required bool CusumChangeDetected;
        public required double CusumConfidence;
        public required int CusumSampleSize;
        public required int CusumEstimatedBreakIndex;

        public required bool BaiPerronIsValid;
        public required int BaiPerronBreakCount;
        public required double BaiPerronConfidence;
        public required int BaiPerronSampleSize;
        public required IReadOnlyList<int> BaiPerronBreakpoints;

        // Derived (pure functions of the raw fields above, computed once in BuildObservations; see the
        // formulas/guards documented in this file's type-level doc comment, items 2-4).
        public required int? CusumAbsoluteBreakBar;
        public required bool CusumIndexAnomaly;

        public required int? BaiPerronMostRecentLocalIndex;
        public required int? BaiPerronMostRecentAbsoluteBreakBar;
        public required bool BaiPerronIndexAnomaly;

        public required int? BreakAgeBars;

        /// <summary>Operational "detected" definition for CUSUM (has a real boolean field).</summary>
        public bool CusumDetected => CusumChangeDetected;

        /// <summary>Operational "detected" definition for Bai-Perron (item 1 above: BreakCount &gt; 0,
        /// stated explicitly since Bai-Perron has no boolean detection field of its own).</summary>
        public bool BaiPerronDetected => BaiPerronBreakCount > 0;
    }

    [Fact]
    public void Descriptive_CusumBaiPerron_OverlapAgeClustering_Lot156()
    {
        try
        {
            // ── Step 1: load the real dataset (same recipe as every prior Lot 15.x/14.x analysis test) ──────
            HistoricalSeries series = _yahoo.Require();
            Assert.True(series.Count > 0);

            string fingerprint = HistoricalSeriesFingerprint.Compute(series);
            const int warmupBars = 128;

            _output.WriteLine("=== DATASET IDENTITY (Lot 15.6, THIS run) ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");
            _output.WriteLine($"DatasetFingerprint={fingerprint}");

            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT15.6-FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            // ── Step 2: run the REAL, unmodified production pipeline exactly once ──────────────────────────
            BacktestSignalPipelineResult signalResult = new BacktestEngine().RunSignalPipeline(scenario, warmupBars);
            _output.WriteLine("");
            _output.WriteLine($"BarsProcessed={signalResult.BarsProcessed}, BarsRejected={signalResult.BarsRejected}, WarmupBars={signalResult.WarmupBars}, ReadyBars={signalResult.ReadyBars}, ExceptionCount={signalResult.ExceptionCount}, TotalSeriesBars={series.Count}");
            Assert.Equal(0, signalResult.ExceptionCount);

            // ── Step 3: extract raw + derived per-bar observations (pure read of .Regime/.Decision.Winner) ──
            List<BarObservation> observations = BuildObservations(signalResult);
            _output.WriteLine("");
            _output.WriteLine($"ObservedReadyBars={observations.Count} (expected == ReadyBars {signalResult.ReadyBars})");
            Assert.Equal(signalResult.ReadyBars, observations.Count);

            int cusumInvalidCount = observations.Count(o => !o.CusumIsValid);
            int baiPerronInvalidCount = observations.Count(o => !o.BaiPerronIsValid);
            int cusumAnomalyCount = observations.Count(o => o.CusumIndexAnomaly);
            int baiPerronAnomalyCount = observations.Count(o => o.BaiPerronIndexAnomaly);
            _output.WriteLine($"CusumInvalidCount={cusumInvalidCount} ({Pct(cusumInvalidCount, observations.Count)}%), BaiPerronInvalidCount={baiPerronInvalidCount} ({Pct(baiPerronInvalidCount, observations.Count)}%)");
            _output.WriteLine($"CusumIndexAnomalyCount={cusumAnomalyCount} (EstimatedBreakIndex=={{SampleSize}} edge case, see type doc item 2), BaiPerronIndexAnomalyCount={baiPerronAnomalyCount} (see type doc item 3, expected 0 given current BaiPerronStatistics implementation)");

            string outputDir = ResolveOutputDirectory();

            // ═══════════════════════════════ AGGREGATION + CSV + PRINT (run #1) ═══════════════════════════
            string hash1 = RunFullAnalysisAndReport(observations, outputDir, print: true);

            // ═══════════════════════ Determinism re-run (brief §28, no re-download, same in-memory data) ════
            string hash2 = RunFullAnalysisAndReport(observations, outputDir, print: false);
            _output.WriteLine("");
            _output.WriteLine($"=== DETERMINISM CHECK (brief §28) === Hash1={hash1}, Hash2={hash2}, Identical={hash1 == hash2}");
            Assert.Equal(hash1, hash2);
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ──────────────────────────────────────────── Observation extraction ──────────────────────────────────

    private static List<BarObservation> BuildObservations(BacktestSignalPipelineResult signalResult)
    {
        var list = new List<BarObservation>(signalResult.ReadyBars);

        foreach (BacktestSignalResult bar in signalResult.Bars)
        {
            if (bar.Status != BacktestSignalStatus.Ready) continue;
            if (bar.Regime is null || bar.Decision is null) continue;

            EvidenceSet ev = bar.Regime;
            CusumResult? cusum = ev.Cusum;
            BaiPerronResult? bp = ev.BaiPerron;

            bool cusumValid = cusum?.IsValid ?? false;
            bool cusumChangeDetected = cusum?.ChangeDetected ?? false;
            double cusumConfidence = cusum?.Confidence ?? 0.0;
            int cusumSampleSize = cusum?.SampleSize ?? 0;
            int cusumEstimatedBreakIndex = cusum?.EstimatedBreakIndex ?? -1;

            bool bpValid = bp?.IsValid ?? false;
            int bpBreakCount = bp?.BreakCount ?? 0;
            double bpConfidence = bp?.Confidence ?? 0.0;
            int bpSampleSize = bp?.SampleSize ?? 0;
            IReadOnlyList<int> bpBreakpoints = bp?.Breakpoints ?? Array.Empty<int>();

            // ── CUSUM absolute-index translation (type doc item 2) ────────────────────────────────────────
            int? cusumAbsolute = null;
            bool cusumAnomaly = false;
            if (cusumChangeDetected && cusumValid)
            {
                if (cusumEstimatedBreakIndex >= 0 && cusumEstimatedBreakIndex < cusumSampleSize)
                    cusumAbsolute = bar.BarIndex - (cusumSampleSize - 1 - cusumEstimatedBreakIndex);
                else
                    cusumAnomaly = true;
            }

            // ── Bai-Perron absolute-index translation, most recent breakpoint (type doc item 3) ────────────
            int? bpMostRecentLocal = null;
            int? bpMostRecentAbsolute = null;
            bool bpAnomaly = false;
            if (bpBreakCount > 0 && bpValid && bpBreakpoints.Count > 0)
            {
                int maxLocal = bpBreakpoints.Max();
                if (maxLocal >= 0 && maxLocal < bpSampleSize)
                {
                    bpMostRecentLocal = maxLocal;
                    bpMostRecentAbsolute = bar.BarIndex - (bpSampleSize - 1 - maxLocal);
                }
                else
                {
                    bpAnomaly = true;
                }
            }

            int? breakAge = bpMostRecentAbsolute.HasValue ? bar.BarIndex - bpMostRecentAbsolute.Value : null;

            list.Add(new BarObservation
            {
                BarIndex = bar.BarIndex,
                Winner = bar.Decision.Winner,

                CusumIsValid = cusumValid,
                CusumChangeDetected = cusumChangeDetected,
                CusumConfidence = cusumConfidence,
                CusumSampleSize = cusumSampleSize,
                CusumEstimatedBreakIndex = cusumEstimatedBreakIndex,

                BaiPerronIsValid = bpValid,
                BaiPerronBreakCount = bpBreakCount,
                BaiPerronConfidence = bpConfidence,
                BaiPerronSampleSize = bpSampleSize,
                BaiPerronBreakpoints = bpBreakpoints,

                CusumAbsoluteBreakBar = cusumAbsolute,
                CusumIndexAnomaly = cusumAnomaly,

                BaiPerronMostRecentLocalIndex = bpMostRecentLocal,
                BaiPerronMostRecentAbsoluteBreakBar = bpMostRecentAbsolute,
                BaiPerronIndexAnomaly = bpAnomaly,

                BreakAgeBars = breakAge
            });
        }

        return list;
    }

    // ──────────────────────────────────────────── Aggregation + CSV ───────────────────────────────────────

    private string RunFullAnalysisAndReport(List<BarObservation> observations, string outputDir, bool print)
    {
        var hashInput = new StringBuilder();

        List<object?[]> overlapRows = BuildOverlapAgreementRows(observations);
        List<object?[]> disagreementRows = BuildDisagreementTimingRows(observations);
        List<object?[]> breakAgeRows = BuildBreakAgeDistributionRows(observations);
        List<object?[]> clusteringRows = BuildClusteringRows(observations);
        List<object?[]> regimeConditionalRows = BuildRegimeConditionalRows(observations);

        WriteCsv(Path.Combine(outputDir, "overlap_agreement.csv"),
            new[] { "Scope", "Category", "Count", "PctOfScope", "TotalInScope" }, overlapRows);
        WriteCsv(Path.Combine(outputDir, "disagreement_timing.csv"),
            new[] { "N_BothDetected", "N_UsedForTiming", "N_ExcludedAnomalous", "MeanAbsDiffBars", "MedianAbsDiffBars", "MinAbsDiffBars", "MaxAbsDiffBars", "StdDevAbsDiffBars" }, disagreementRows);
        WriteCsv(Path.Combine(outputDir, "break_age_distribution.csv"),
            new[] { "RowType", "Label", "N", "Mean", "Median", "P10", "P25", "P75", "P90", "Min", "Max" }, breakAgeRows);
        WriteCsv(Path.Combine(outputDir, "clustering.csv"),
            new[] { "Evidence", "Scope", "RunCount", "MeanRunLength", "MedianRunLength", "MaxRunLength", "TotalDetectedBars", "TotalBarsInScope" }, clusteringRows);
        WriteCsv(Path.Combine(outputDir, "regime_conditional.csv"),
            new[] { "Regime", "N", "PctCusumDetected", "PctBaiPerronDetected", "MeanCusumConfidence", "MeanBaiPerronConfidence", "MeanBaiPerronBreakCount", "MeanBreakAgeBarsAmongDetected", "ReliabilityFlag" }, regimeConditionalRows);

        AppendHash(hashInput, overlapRows);
        AppendHash(hashInput, disagreementRows);
        AppendHash(hashInput, breakAgeRows);
        AppendHash(hashInput, clusteringRows);
        AppendHash(hashInput, regimeConditionalRows);

        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== SECTION 1: OVERLAP_AGREEMENT (detected := Cusum.ChangeDetected / BaiPerron.BreakCount>0, see type doc item 1) ===");
            _output.WriteLine("Scope | Category | Count | PctOfScope% | TotalInScope");
            foreach (object?[] r in overlapRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));

            _output.WriteLine("");
            _output.WriteLine("=== SECTION 2: DISAGREEMENT_TIMING (|cusumAbsoluteBreakBar - baiPerronMostRecentAbsoluteBreakBar|, bars where BOTH detected) ===");
            _output.WriteLine("N_BothDetected | N_UsedForTiming | N_ExcludedAnomalous | MeanAbsDiffBars | MedianAbsDiffBars | MinAbsDiffBars | MaxAbsDiffBars | StdDevAbsDiffBars");
            foreach (object?[] r in disagreementRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));

            _output.WriteLine("");
            _output.WriteLine("=== SECTION 3: BREAK_AGE_DISTRIBUTION (BreakAgeBars = currentBarIndex - mostRecentAbsoluteBreakpointBar, Bai-Perron only) ===");
            _output.WriteLine("RowType | Label | N | Mean | Median | P10 | P25 | P75 | P90 | Min | Max   [DISTRIBUTION rows: BreakAgeBars stats. BINNED_CONFIDENCE rows: N + MeanConfidence in the Mean column. CORRELATION row: Pearson coefficient in the Mean column.]");
            foreach (object?[] r in breakAgeRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));

            _output.WriteLine("");
            _output.WriteLine("=== SECTION 5: CLUSTERING (run-length of the detection boolean; 'by regime' = run lengths computed on the chronological subsequence restricted to bars where Winner==that regime, i.e. non-regime bars are spliced out - explicit choice, see method doc) ===");
            _output.WriteLine("Evidence | Scope | RunCount | MeanRunLength | MedianRunLength | MaxRunLength | TotalDetectedBars | TotalBarsInScope");
            foreach (object?[] r in clusteringRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));

            _output.WriteLine("");
            _output.WriteLine("=== SECTION 6: REGIME_CONDITIONAL (N<30 flagged LOW_N_UNRELIABLE per this lot's own discipline) ===");
            _output.WriteLine("Regime | N | PctCusumDetected% | PctBaiPerronDetected% | MeanCusumConfidence | MeanBaiPerronConfidence | MeanBaiPerronBreakCount | MeanBreakAgeBarsAmongDetected | ReliabilityFlag");
            foreach (object?[] r in regimeConditionalRows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Sanity: no NaN/Infinity in any DESCRIPTIVE numeric cell. Correlation cells are explicitly
        // exempted - a Pearson correlation over a near/exactly-zero-variance input is legitimately NaN,
        // and reporting that NaN honestly is more correct than hiding or crashing on it. ─────────────────
        foreach (object?[] row in overlapRows.Concat(disagreementRows).Concat(clusteringRows).Concat(regimeConditionalRows))
            foreach (object? cell in row)
                if (cell is double d) Assert.True(double.IsFinite(d), $"NaN/Infinity found in a report cell: {d}");
        foreach (object?[] row in breakAgeRows)
        {
            bool isCorrelationRow = (string)row[0]! == "CORRELATION";
            for (int i = 0; i < row.Length; i++)
                if (row[i] is double d && !(isCorrelationRow && i == 3))
                    Assert.True(double.IsFinite(d), $"NaN/Infinity found in a report cell: {d}");
        }

        return Sha256Hex(hashInput.ToString());
    }

    // ── Section 1: OVERLAP_AGREEMENT ────────────────────────────────────────────────────────────────────

    private static List<object?[]> BuildOverlapAgreementRows(List<BarObservation> observations)
    {
        var rows = new List<object?[]>();
        AddOverlapRows("ALL", observations, rows);
        foreach (MarketState regime in AllRegimes)
            AddOverlapRows(regime.ToString(), observations.Where(o => o.Winner == regime).ToList(), rows);
        return rows;
    }

    private static void AddOverlapRows(string scope, List<BarObservation> bars, List<object?[]> rows)
    {
        int total = bars.Count;
        int both = bars.Count(o => o.CusumDetected && o.BaiPerronDetected);
        int cusumOnly = bars.Count(o => o.CusumDetected && !o.BaiPerronDetected);
        int bpOnly = bars.Count(o => !o.CusumDetected && o.BaiPerronDetected);
        int neither = bars.Count(o => !o.CusumDetected && !o.BaiPerronDetected);

        rows.Add(new object?[] { scope, "Both", both, Pct(both, total), total });
        rows.Add(new object?[] { scope, "CusumOnly", cusumOnly, Pct(cusumOnly, total), total });
        rows.Add(new object?[] { scope, "BaiPerronOnly", bpOnly, Pct(bpOnly, total), total });
        rows.Add(new object?[] { scope, "Neither", neither, Pct(neither, total), total });
    }

    // ── Section 2: DISAGREEMENT_TIMING ──────────────────────────────────────────────────────────────────

    private static List<object?[]> BuildDisagreementTimingRows(List<BarObservation> observations)
    {
        List<BarObservation> bothDetected = observations.Where(o => o.CusumDetected && o.BaiPerronDetected).ToList();
        List<BarObservation> usable = bothDetected
            .Where(o => o.CusumAbsoluteBreakBar.HasValue && o.BaiPerronMostRecentAbsoluteBreakBar.HasValue)
            .ToList();
        int excluded = bothDetected.Count - usable.Count;

        List<double> diffs = usable
            .Select(o => (double)Math.Abs(o.CusumAbsoluteBreakBar!.Value - o.BaiPerronMostRecentAbsoluteBreakBar!.Value))
            .OrderBy(v => v)
            .ToList();

        var rows = new List<object?[]>
        {
            new object?[]
            {
                bothDetected.Count,
                usable.Count,
                excluded,
                diffs.Count > 0 ? Math.Round(diffs.Average(), 6) : (double?)null,
                diffs.Count > 0 ? Math.Round(Percentile(diffs, 0.5), 6) : (double?)null,
                diffs.Count > 0 ? diffs.Min() : (double?)null,
                diffs.Count > 0 ? diffs.Max() : (double?)null,
                diffs.Count > 0 ? Math.Round(StdDev(diffs), 6) : (double?)null
            }
        };
        return rows;
    }

    // ── Section 3: BREAK_AGE_DISTRIBUTION ───────────────────────────────────────────────────────────────

    private static readonly (double LowerInclusive, double UpperExclusive, string Label)[] BreakAgeBins =
    {
        (0, 20, "[0,20)"), (20, 50, "[20,50)"), (50, 90, "[50,90)"), (90, double.PositiveInfinity, "[90,127]")
    };

    private static List<object?[]> BuildBreakAgeDistributionRows(List<BarObservation> observations)
    {
        var rows = new List<object?[]>();
        List<BarObservation> withAge = observations.Where(o => o.BreakAgeBars.HasValue).ToList();

        AddDistributionRow("ALL", withAge, rows);
        foreach (MarketState regime in AllRegimes)
            AddDistributionRow(regime.ToString(), withAge.Where(o => o.Winner == regime).ToList(), rows);

        foreach (var bin in BreakAgeBins)
        {
            List<BarObservation> inBin = withAge
                .Where(o => o.BreakAgeBars!.Value >= bin.LowerInclusive && o.BreakAgeBars!.Value < bin.UpperExclusive)
                .ToList();
            double? meanConfidence = inBin.Count > 0 ? Math.Round(inBin.Average(o => o.BaiPerronConfidence), 6) : null;
            rows.Add(new object?[] { "BINNED_CONFIDENCE", bin.Label, inBin.Count, meanConfidence, null, null, null, null, null, null, null });
        }

        double correlation = Correlation(withAge.Select(o => (double)o.BreakAgeBars!.Value), withAge.Select(o => o.BaiPerronConfidence));
        rows.Add(new object?[] { "CORRELATION", "Pearson(BreakAgeBars,BaiPerron.Confidence)", withAge.Count, Math.Round(correlation, 6), null, null, null, null, null, null, null });

        return rows;
    }

    private static void AddDistributionRow(string label, List<BarObservation> bars, List<object?[]> rows)
    {
        if (bars.Count == 0)
        {
            rows.Add(new object?[] { "DISTRIBUTION", label, 0, null, null, null, null, null, null, null, null });
            return;
        }

        List<double> ages = bars.Select(o => (double)o.BreakAgeBars!.Value).OrderBy(v => v).ToList();
        rows.Add(new object?[]
        {
            "DISTRIBUTION", label, ages.Count,
            Math.Round(ages.Average(), 6),
            Math.Round(Percentile(ages, 0.5), 6),
            Math.Round(Percentile(ages, 0.10), 6),
            Math.Round(Percentile(ages, 0.25), 6),
            Math.Round(Percentile(ages, 0.75), 6),
            Math.Round(Percentile(ages, 0.90), 6),
            ages.Min(),
            ages.Max()
        });
    }

    // ── Section 5: CLUSTERING ────────────────────────────────────────────────────────────────────────────

    /// <summary>"By regime" run-length definition (stated explicitly - the brief does not fully
    /// disambiguate this for a non-regime-membership boolean): the chronological subsequence of bars where
    /// <c>Winner == regime</c> is extracted (preserving relative order, but with non-matching bars spliced
    /// out - i.e. two regime bars that are chronologically adjacent WITHIN that subsequence are treated as
    /// run-adjacent even if other-regime bars separated them in the original timeline). This differs from,
    /// e.g., the StructuralBreak run-length measured in Lot 15.0/15.2 - those measured a MarketState
    /// MEMBERSHIP boolean over the untouched full timeline (no splicing possible: leaving the regime always
    /// breaks its own run). Here the flag is evidence detection, independent of regime membership, so the
    /// two notions of "run" are genuinely different and must be defined explicitly.</summary>
    private static List<object?[]> BuildClusteringRows(List<BarObservation> observations)
    {
        var rows = new List<object?[]>();
        (string Name, Func<BarObservation, bool> Detected)[] evidences =
        {
            ("Cusum", o => o.CusumDetected),
            ("BaiPerron", o => o.BaiPerronDetected)
        };

        foreach (var (name, detected) in evidences)
        {
            AddClusteringRow(name, "ALL", observations, detected, rows);
            foreach (MarketState regime in AllRegimes)
                AddClusteringRow(name, regime.ToString(), observations.Where(o => o.Winner == regime).ToList(), detected, rows);
        }
        return rows;
    }

    private static void AddClusteringRow(string evidence, string scope, List<BarObservation> bars, Func<BarObservation, bool> detected, List<object?[]> rows)
    {
        List<bool> flags = bars.Select(detected).ToList();
        List<int> runs = ComputeRuns(flags);
        int totalDetected = flags.Count(f => f);

        rows.Add(new object?[]
        {
            evidence, scope, runs.Count,
            runs.Count > 0 ? Math.Round(runs.Average(), 6) : (double?)null,
            runs.Count > 0 ? Math.Round(Percentile(runs.Select(v => (double)v).OrderBy(v => v).ToList(), 0.5), 6) : (double?)null,
            runs.Count > 0 ? runs.Max() : (int?)null,
            totalDetected,
            bars.Count
        });
    }

    private static List<int> ComputeRuns(IReadOnlyList<bool> flags)
    {
        var runs = new List<int>();
        int i = 0;
        while (i < flags.Count)
        {
            if (!flags[i]) { i++; continue; }
            int start = i;
            while (i < flags.Count && flags[i]) i++;
            runs.Add(i - start);
        }
        return runs;
    }

    // ── Section 6: REGIME_CONDITIONAL ───────────────────────────────────────────────────────────────────

    private static List<object?[]> BuildRegimeConditionalRows(List<BarObservation> observations)
    {
        var rows = new List<object?[]>();
        foreach (MarketState regime in AllRegimes)
        {
            List<BarObservation> bars = observations.Where(o => o.Winner == regime).ToList();
            int n = bars.Count;
            int cusumDetectedCount = bars.Count(o => o.CusumDetected);
            int bpDetectedCount = bars.Count(o => o.BaiPerronDetected);
            List<BarObservation> withAge = bars.Where(o => o.BreakAgeBars.HasValue).ToList();

            rows.Add(new object?[]
            {
                regime.ToString(), n,
                Pct(cusumDetectedCount, n),
                Pct(bpDetectedCount, n),
                n > 0 ? Math.Round(bars.Average(o => o.CusumConfidence), 6) : (double?)null,
                n > 0 ? Math.Round(bars.Average(o => o.BaiPerronConfidence), 6) : (double?)null,
                n > 0 ? Math.Round(bars.Average(o => o.BaiPerronBreakCount), 6) : (double?)null,
                withAge.Count > 0 ? Math.Round(withAge.Average(o => o.BreakAgeBars!.Value), 6) : (double?)null,
                n < 30 ? "LOW_N_UNRELIABLE" : "OK"
            });
        }
        return rows;
    }

    // ─────────────────────────────────────────────── Generic helpers ──────────────────────────────────────

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

    private static string ResolveOutputDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;

        if (dir is null)
            throw new InvalidOperationException("Could not locate IQIAIndicator.Tests.csproj to resolve the StructuralBreakAudit output directory.");

        string outputDir = Path.Combine(dir, "Research", "StructuralBreakAudit", "Output");
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
        exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException;
}
