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
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;
using IQIAIndicator.Engine.Risk;
using Xunit;
using FusionEngine = IQIAIndicator.Engine.Fusion.EvidenceFusionEngine;
using IQIAIndicator.Tests.BacktestTests.Yahoo;

namespace IQIAIndicator.Tests.Research.Lot16ContractNormalization;

/// <summary>
/// LOT 16.1 — Phase 0 + Phase 1. OBSERVATION ONLY, no production type modified.
///
/// Phase 0: measure, on the real Yahoo MES M5 dataset, with the code AS IT EXISTS AFTER LOT 16,
///   corr(FusionDimension.StructuralBreak.Value, FusionDimension.StructuralBreak.Confidence)
/// on RAW (post-EvidenceFusionEngine) and STABLE (post-FusionStateManager). Deterministic (double hash).
///
/// Phase 1: for every bar, evaluate candidate sources for a Confidence DISTINCT from Strength, and
/// report their distribution, saturation, Pearson corr with the new Strength, and Spearman corr with
/// the raw CUSUM ratio r (a candidate whose Spearman(r) ~ 1 is just Strength re-expressed).
///   A_agreement       : Both -> 1.0 ; CusumOnly/BaiPerronOnly -> 0.5 ; Neither/Unavailable -> 0.0
///   B_oldRampClamp     : Math.Clamp(cusum.Confidence, 0, 1)   (the value Strength stopped using at Lot 16)
///   C_breakCountNorm   : BreakCountMagnitude / 8              (Bai-Perron BreakCount range 1..8)
///   D_sampleAdequacy   : min(1, Cusum.SampleSize / 30)        (CUSUM window size in RegimeEngine)
///   E_locStableInstant : 1.0 if EstimatedBreakIndex == previous bar's, else 0.0 (needs the chronological walk)
/// </summary>
public sealed class Lot161ConfidenceSourceComparisonTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public Lot161ConfidenceSourceComparisonTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static readonly string[] CandidateNames = { "A_agreement", "B_oldRampClamp", "C_breakCountNorm", "D_sampleAdequacy", "E_locStableInstant" };

    private static FusionEngine ProductionFusion() => new(new IFusionRule[]
    {
        new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new RandomWalkRule(),
        new StructuralBreakEvidenceRule()
    });

    private sealed class Row
    {
        public required int BarIndex;
        public required bool Detected;
        public required double RawRatio;
        public required double RawValue;        // production RAW StructuralBreak.Value  (== new Strength)
        public required double RawConfidence;   // production RAW StructuralBreak.Confidence
        public required double StableValue;
        public required double StableConfidence;
        public required double[] Cand;
    }

    [Fact]
    public void Integration_Network_Lot161_ConfidenceSource_Phase0And1()
    {
        try
        {
            HistoricalSeries series = _yahoo.Require();
            Assert.True(series.Count > 0);

            string fingerprint = HistoricalSeriesFingerprint.Compute(series);
            const int warmupBars = 128;

            _output.WriteLine("=== LOT 16.1 DATASET IDENTITY ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");
            _output.WriteLine($"Fingerprint={fingerprint}");

            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT16.1-CMP", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            BacktestSignalPipelineResult run = new BacktestEngine().RunSignalPipeline(scenario, warmupBars);
            Assert.Equal(0, run.ExceptionCount);

            List<Row> rows = BuildRows(run, series);
            List<Row> rowsRerun = BuildRows(run, series);
            _output.WriteLine($"ReadyBars={run.ReadyBars}, Rows={rows.Count}, Detected={rows.Count(r => r.Detected)}");
            Assert.True(rows.Count > 1000);

            // ── PHASE 0: corr(Value, Confidence) RAW + STABLE, post-Lot-16 ────────────────────────────
            double corrRaw = Pearson(rows.Select(r => r.RawValue).ToList(), rows.Select(r => r.RawConfidence).ToList());
            double corrStable = Pearson(rows.Select(r => r.StableValue).ToList(), rows.Select(r => r.StableConfidence).ToList());
            int rawIdentical = rows.Count(r => BitConverter.DoubleToInt64Bits(r.RawValue) == BitConverter.DoubleToInt64Bits(r.RawConfidence));
            int stableIdentical = rows.Count(r => BitConverter.DoubleToInt64Bits(r.StableValue) == BitConverter.DoubleToInt64Bits(r.StableConfidence));
            _output.WriteLine("");
            _output.WriteLine("=== PHASE 0: corr(StructuralBreak.Value, StructuralBreak.Confidence) POST-LOT-16 ===");
            _output.WriteLine($"RAW    : Pearson={corrRaw:G17}  bit-identical pairs={rawIdentical}/{rows.Count}");
            _output.WriteLine($"STABLE : Pearson={corrStable:G17}  bit-identical pairs={stableIdentical}/{rows.Count}");
            _output.WriteLine($"PHASE 0 RESULT: {(corrRaw >= 0.98 ? "CONFIRMED PERSISTENT" : "ALREADY RESOLVED BY LOT 16")}");

            // ── PHASE 1: candidate source evaluation ─────────────────────────────────────────────────
            string outDir = ResolveOutputDirectory();
            string h1 = Aggregate(rows, corrRaw, corrStable, outDir, print: true);
            string h2 = Aggregate(rowsRerun, corrRaw, corrStable, outDir, print: false);
            _output.WriteLine("");
            _output.WriteLine($"=== DETERMINISM === Hash1={h1} Hash2={h2} Identical={h1 == h2}");
            Assert.Equal(h1, h2);
        }
        catch (Exception exception) when (exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private List<Row> BuildRows(BacktestSignalPipelineResult run, HistoricalSeries series)
    {
        FusionEngine prod = ProductionFusion();
        var prodState = new FusionStateManager();
        var rows = new List<Row>(series.Count);
        int prevBreakIndex = int.MinValue;

        foreach (BacktestSignalResult bar in run.Bars)
        {
            if (bar.Status is BacktestSignalStatus.Rejected or BacktestSignalStatus.Exception) continue;
            if (bar.Regime is null) continue;

            FusionContext ctx = new()
            {
                Evidence = bar.Regime, Timestamp = bar.Timestamp, Symbol = series.Symbol,
                TimeFrame = series.TimeFrame, EvaluationId = Guid.Empty
            };
            FusionResult raw = prod.Fuse(ctx);
            FusionSnapshot snap = prodState.Update(raw, bar.Timestamp);

            if (bar.Status != BacktestSignalStatus.Ready) continue;

            CusumResult? cusum = bar.Regime.Cusum;
            if (cusum is null || !cusum.IsValid) continue;

            StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusum, bar.Regime.BaiPerron);

            double peak = Math.Max(cusum.PositiveCusum, Math.Abs(cusum.NegativeCusum));
            double r = cusum.Threshold > 0.0 ? peak / cusum.Threshold : 0.0;

            double aAgreement = contract.Agreement switch
            {
                StructuralBreakAgreement.Both => 1.0,
                StructuralBreakAgreement.CusumOnly => 0.5,
                StructuralBreakAgreement.BaiPerronOnly => 0.5,
                _ => 0.0
            };
            double bOldRamp = Math.Clamp(cusum.Confidence, 0.0, 1.0);
            double cBreakCount = Math.Clamp(contract.BreakCountMagnitude / 8.0, 0.0, 1.0);
            double dSampleAdequacy = Math.Clamp(cusum.SampleSize / 30.0, 0.0, 1.0);
            double eLocStable = prevBreakIndex != int.MinValue && cusum.EstimatedBreakIndex == prevBreakIndex ? 1.0 : 0.0;
            prevBreakIndex = cusum.EstimatedBreakIndex;

            FusionConfidence rawSb = raw.Dimensions[FusionDimension.StructuralBreak];
            FusionConfidence stableSb = snap.StableResult.Dimensions[FusionDimension.StructuralBreak];

            rows.Add(new Row
            {
                BarIndex = bar.BarIndex,
                Detected = cusum.ChangeDetected,
                RawRatio = r,
                RawValue = rawSb.Value,
                RawConfidence = rawSb.Confidence,
                StableValue = stableSb.Value,
                StableConfidence = stableSb.Confidence,
                Cand = new[] { aAgreement, bOldRamp, cBreakCount, dSampleAdequacy, eLocStable }
            });
        }

        return rows;
    }

    private string Aggregate(List<Row> rows, double corrRaw, double corrStable, string outDir, bool print)
    {
        var hash = new StringBuilder();
        var summary = new List<object?[]>();

        List<double> strength = rows.Select(r => r.RawValue).ToList();
        List<double> ratio = rows.Select(r => r.RawRatio).ToList();
        List<double> ratioRank = Ranks(ratio);

        summary.Add(new object?[] { "PHASE0_corr_Value_Confidence_RAW", R(corrRaw) });
        summary.Add(new object?[] { "PHASE0_corr_Value_Confidence_STABLE", R(corrStable) });

        for (int c = 0; c < CandidateNames.Length; c++)
        {
            int ci = c;
            List<double> v = rows.Select(x => x.Cand[ci]).ToList();
            double mean = v.Average();
            double var = v.Count > 1 ? v.Sum(x => (x - mean) * (x - mean)) / (v.Count - 1) : 0.0;
            int sat = v.Count(x => x >= 0.9999);
            int zero = v.Count(x => x <= 1e-9);
            int distinct = v.Select(x => Math.Round(x, 9)).Distinct().Count();
            double corrStrength = Pearson(strength, v);
            double spearmanRatio = Pearson(ratioRank, Ranks(v)); // Spearman(candidate, raw ratio r)
            int transitions = 0;
            for (int i = 1; i < rows.Count; i++)
                if (Math.Abs(rows[i].Cand[ci] - rows[i - 1].Cand[ci]) > 1e-9) transitions++;

            summary.Add(new object?[]
            {
                CandidateNames[ci], v.Count, R(v.Min()), R(v.Max()), R(mean), R(Median(v)), R(Math.Sqrt(var)), R(var),
                distinct, R(100.0 * sat / v.Count), R(100.0 * zero / v.Count),
                R(corrStrength), R(spearmanRatio),
                transitions, R(100.0 * transitions / Math.Max(1, rows.Count - 1))
            });
        }

        WriteCsv(Path.Combine(outDir, "lot161_confidence_candidates.csv"),
            new[] { "Candidate", "N", "Min", "Max", "Mean", "Median", "Std", "Var", "Distinct",
                "SatPct(>=0.9999)", "ZeroPct", "PearsonWithStrength", "SpearmanWithRawRatio", "Transitions", "TransitionPct" },
            summary);
        AppendHash(hash, summary);

        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== PHASE 1: CANDIDATE CONFIDENCE SOURCES ===");
            _output.WriteLine("Candidate | N | Min | Max | Mean | Median | Std | Var | Distinct | Sat% | Zero% | Pearson(Strength) | Spearman(r) | Trans% ");
            foreach (object?[] rr in summary.Where(s => ((string)s[0]!).StartsWith("A_") || ((string)s[0]!).StartsWith("B_") || ((string)s[0]!).StartsWith("C_") || ((string)s[0]!).StartsWith("D_") || ((string)s[0]!).StartsWith("E_")))
                _output.WriteLine(string.Join(" | ", new[] { rr[0], rr[1], rr[2], rr[3], rr[4], rr[5], rr[6], rr[7], rr[8], rr[9], rr[10], rr[11], rr[12], rr[14] }.Select(F)));
        }

        foreach (object?[] row in summary)
            foreach (object? cell in row)
                if (cell is double d) Assert.True(double.IsFinite(d), $"non-finite cell {d}");

        return Sha256Hex(hash.ToString());
    }

    // ── helpers ──
    private static double R(double x) => double.IsFinite(x) ? Math.Round(x, 8) : x;
    private static double Median(List<double> xs)
    {
        if (xs.Count == 0) return 0;
        var s = xs.OrderBy(v => v).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : 0.5 * (s[s.Count / 2 - 1] + s[s.Count / 2]);
    }
    private static double Pearson(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        int n = Math.Min(a.Count, b.Count);
        if (n < 2) return 0;
        double ma = a.Take(n).Average(), mb = b.Take(n).Average(), sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < n; i++) { double da = a[i] - ma, db = b[i] - mb; sab += da * db; saa += da * da; sbb += db * db; }
        double den = Math.Sqrt(saa * sbb);
        return den <= 1e-15 ? 0 : sab / den;
    }
    private static List<double> Ranks(IReadOnlyList<double> xs)
    {
        var idx = Enumerable.Range(0, xs.Count).OrderBy(i => xs[i]).ToList();
        var ranks = new double[xs.Count];
        int i2 = 0;
        while (i2 < idx.Count)
        {
            int j = i2;
            while (j + 1 < idx.Count && xs[idx[j + 1]] == xs[idx[i2]]) j++;
            double avg = (i2 + j) / 2.0;
            for (int k = i2; k <= j; k++) ranks[idx[k]] = avg;
            i2 = j + 1;
        }
        return ranks.ToList();
    }
    private static string ResolveOutputDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) throw new InvalidOperationException("Could not locate IQIAIndicator.Tests.csproj.");
        string outDir = Path.Combine(dir, "Research", "Lot16ContractNormalization", "Output");
        Directory.CreateDirectory(outDir);
        return outDir;
    }
    private static void WriteCsv(string path, string[] headers, List<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Csv)));
        foreach (object?[] row in rows)
        {
            var padded = new object?[headers.Length];
            for (int i = 0; i < headers.Length; i++) padded[i] = i < row.Length ? row[i] : null;
            sb.AppendLine(string.Join(",", padded.Select(Csv)));
        }
        File.WriteAllText(path, sb.ToString());
    }
    private static string Csv(object? v) { string s = F(v); return s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }
    private static string F(object? v) => v switch
    {
        null => "",
        double d => d.ToString("G17", CultureInfo.InvariantCulture),
        _ => v.ToString() ?? ""
    };
    private static void AppendHash(StringBuilder sb, List<object?[]> rows)
    {
        foreach (object?[] row in rows) sb.Append(string.Join("|", row.Select(F))).Append(';');
    }
    private static string Sha256Hex(string s)
    {
        byte[] b = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(b.Length * 2);
        foreach (byte x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }
}
