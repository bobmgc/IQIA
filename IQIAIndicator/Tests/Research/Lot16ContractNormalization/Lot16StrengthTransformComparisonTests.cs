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
/// LOT 16 — Phase 3/4. OBSERVATION ONLY, no production type modified. Runs the unmodified signal
/// pipeline over real Yahoo MES M5, reconstructs the pre-clamp CUSUM ratio r = peak/threshold from the
/// already-exposed CusumResult fields (PositiveCusum / NegativeCusum / Threshold — no change to
/// CusumStatistics), and compares several parameter-free bounded monotone transforms of r as candidate
/// replacements for the saturating Strength = Clamp(cusum.Confidence, 0, 1).
///
/// Candidates (all: f(0)=0, monotone increasing on [0,inf), bounded [0,1), deterministic, pure per-bar):
///   C0  OLD  = Clamp(r, 0, 1)                       (baseline — saturates at 1.0 for every detected bar)
///   C1       = r / (1 + r)                          (rational soft-saturation; f(1)=0.5 at the Page-CUSUM
///                                                    detection boundary r=1 — anchor is the pre-existing
///                                                    threshold, not a new constant)
///   C2       = tanh(r)
///   C3       = 1 - exp(-r)
///   C4       = (2/pi) * atan(r)
///   C5       = r / sqrt(1 + r*r)
///   C6       = L / (1 + L),  L = ln(1 + r)          (log-scale soft-saturation; heaviest tail)
/// </summary>
public sealed class Lot16StrengthTransformComparisonTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public Lot16StrengthTransformComparisonTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static readonly string[] CandidateNames = { "C0_old_clamp", "C1_r_over_1plusr", "C2_tanh", "C3_1minus_exp", "C4_atan", "C5_r_over_sqrt", "C6_log_soft" };

    private static double[] Candidates(double r)
    {
        double l = Math.Log(1.0 + r);
        return new[]
        {
            Math.Clamp(r, 0.0, 1.0),
            r / (1.0 + r),
            Math.Tanh(r),
            1.0 - Math.Exp(-r),
            (2.0 / Math.PI) * Math.Atan(r),
            r / Math.Sqrt(1.0 + r * r),
            l / (1.0 + l)
        };
    }

    private static readonly FusionDimension[] OtherDims =
    {
        FusionDimension.Stationarity, FusionDimension.Persistence, FusionDimension.MeanReversion, FusionDimension.RandomWalk
    };

    private sealed class Row
    {
        public required int BarIndex;
        public required bool Detected;
        public required double RawRatio;
        public required double[] Cand;
        public required double[] OtherRaw;
    }

    [Fact]
    public void Integration_Network_Lot16_StrengthTransform_Comparison()
    {
        try
        {
            HistoricalSeries series = _yahoo.Require();
            Assert.True(series.Count > 0);

            string fingerprint = HistoricalSeriesFingerprint.Compute(series);
            const int warmupBars = 128;

            _output.WriteLine("=== DATASET IDENTITY ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");
            _output.WriteLine($"Fingerprint={fingerprint}");

            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT16-CMP", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            BacktestSignalPipelineResult signalResult = new BacktestEngine().RunSignalPipeline(scenario, warmupBars);
            Assert.Equal(0, signalResult.ExceptionCount);
            _output.WriteLine($"BarsProcessed={signalResult.BarsProcessed}, ReadyBars={signalResult.ReadyBars}");

            var fusionEngine = new FusionEngine(new IFusionRule[]
            {
                new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new RandomWalkRule()
            });

            var rows = new List<Row>(series.Count);
            int validCusum = 0;

            foreach (BacktestSignalResult bar in signalResult.Bars)
            {
                if (bar.Status != BacktestSignalStatus.Ready) continue;
                if (bar.Regime is null) continue;

                FusionResult raw = fusionEngine.Fuse(new FusionContext
                {
                    Evidence = bar.Regime, Timestamp = bar.Timestamp, Symbol = series.Symbol,
                    TimeFrame = series.TimeFrame, EvaluationId = Guid.Empty
                });

                CusumResult? cusum = bar.Regime.Cusum;
                if (cusum is null || !cusum.IsValid) continue;
                validCusum++;

                double peak = Math.Max(cusum.PositiveCusum, Math.Abs(cusum.NegativeCusum));
                double r = cusum.Threshold > 0.0 ? peak / cusum.Threshold : 0.0;

                rows.Add(new Row
                {
                    BarIndex = bar.BarIndex,
                    Detected = cusum.ChangeDetected,
                    RawRatio = r,
                    Cand = Candidates(r),
                    OtherRaw = OtherDims.Select(d => Get(raw, d).Value).ToArray()
                });
            }

            _output.WriteLine($"ValidCusumBars={validCusum}, Rows={rows.Count}, Detected={rows.Count(x => x.Detected)}");
            Assert.True(rows.Count > 1000);

            string outDir = ResolveOutputDirectory();
            string h1 = Aggregate(rows, outDir, print: true);
            string h2 = Aggregate(rows, outDir, print: false);
            _output.WriteLine($"=== DETERMINISM === Hash1={h1} Hash2={h2} Identical={h1 == h2}");
            Assert.Equal(h1, h2);
        }
        catch (Exception exception) when (exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private string Aggregate(List<Row> rows, string outDir, bool print)
    {
        var hash = new StringBuilder();
        var dist = new List<object?[]>();

        // ---- raw ratio distribution ----
        List<double> allR = rows.Select(x => x.RawRatio).OrderBy(v => v).ToList();
        List<double> detR = rows.Where(x => x.Detected).Select(x => x.RawRatio).OrderBy(v => v).ToList();
        dist.Add(new object?[] { "RAW_RATIO", "all", allR.Count, R(allR.Min()), R(allR.Max()), R(allR.Average()), R(Med(allR)), R(Var(allR)), R(Q(allR, .1)), R(Q(allR, .5)), R(Q(allR, .9)), R(Q(allR, .99)) });
        dist.Add(new object?[] { "RAW_RATIO", "detected", detR.Count, R(detR.Min()), R(detR.Max()), R(detR.Average()), R(Med(detR)), R(Var(detR)), R(Q(detR, .1)), R(Q(detR, .5)), R(Q(detR, .9)), R(Q(detR, .99)) });

        // ---- per-candidate ----
        var summary = new List<object?[]>();
        for (int c = 0; c < CandidateNames.Length; c++)
        {
            int ci = c;
            List<double> all = rows.Select(x => x.Cand[ci]).ToList();
            List<double> det = rows.Where(x => x.Detected).Select(x => x.Cand[ci]).ToList();
            List<double> nd = rows.Where(x => !x.Detected).Select(x => x.Cand[ci]).ToList();

            double satAll = all.Count(v => v >= 0.9999) / (double)all.Count;
            double satDet = det.Count == 0 ? 0 : det.Count(v => v >= 0.9999) / (double)det.Count;

            // monotonicity: order rows by raw ratio, count strict decreases in candidate value
            var ordered = rows.OrderBy(x => x.RawRatio).Select(x => x.Cand[ci]).ToList();
            int monoViol = 0; double maxDrop = 0;
            for (int i = 1; i < ordered.Count; i++)
            {
                double d = ordered[i - 1] - ordered[i];
                if (d > 1e-12) { monoViol++; maxDrop = Math.Max(maxDrop, d); }
            }

            // temporal (chronological order preserved in rows)
            int trans = 0, frozenRunMax = 1, frozenRunCur = 1; var frozenRuns = new List<int>();
            for (int i = 1; i < rows.Count; i++)
            {
                if (Math.Abs(rows[i].Cand[ci] - rows[i - 1].Cand[ci]) > 1e-9) { trans++; frozenRuns.Add(frozenRunCur); frozenRunCur = 1; }
                else { frozenRunCur++; frozenRunMax = Math.Max(frozenRunMax, frozenRunCur); }
            }
            frozenRuns.Add(frozenRunCur);

            double corrRaw = Pearson(rows.Select(x => x.RawRatio).ToList(), all);

            var distSorted = det.OrderBy(v => v).ToList();
            summary.Add(new object?[]
            {
                CandidateNames[ci],
                all.Count, R(all.Min()), R(all.Max()), R(all.Average()), R(Med(all)), R(Std(all)), R(Var(all)),
                det.Count, R(det.Count == 0 ? 0 : det.Min()), R(det.Count == 0 ? 0 : det.Max()),
                R(det.Count == 0 ? 0 : det.Average()), R(det.Count == 0 ? 0 : Med(det)),
                R(det.Count == 0 ? 0 : Std(det)), R(det.Count == 0 ? 0 : Var(det)),
                R(satAll * 100.0), R(satDet * 100.0),
                Distinct(det),
                R(distSorted.Count == 0 ? 0 : Q(distSorted, .1)), R(distSorted.Count == 0 ? 0 : Q(distSorted, .25)),
                R(distSorted.Count == 0 ? 0 : Q(distSorted, .5)), R(distSorted.Count == 0 ? 0 : Q(distSorted, .75)),
                R(distSorted.Count == 0 ? 0 : Q(distSorted, .9)), R(distSorted.Count == 0 ? 0 : Q(distSorted, .99)),
                monoViol, R(maxDrop),
                trans, R(100.0 * trans / Math.Max(1, rows.Count - 1)), frozenRunMax, R(frozenRuns.Average()),
                R(corrRaw),
                R(nd.Count == 0 ? 0 : nd.Average()), R(nd.Count == 0 ? 0 : Std(nd))
            });
        }

        WriteCsv(Path.Combine(outDir, "lot16_raw_ratio_distribution.csv"),
            new[] { "Scope", "Subset", "N", "Min", "Max", "Mean", "Median", "Variance", "P10", "P50", "P90", "P99" }, dist);
        WriteCsv(Path.Combine(outDir, "lot16_candidate_summary.csv"),
            new[] { "Candidate", "N", "Min", "Max", "Mean", "Median", "Std", "Var",
                "DetN", "DetMin", "DetMax", "DetMean", "DetMedian", "DetStd", "DetVar",
                "SatPctAll", "SatPctDetected", "DetDistinct",
                "DetP10", "DetP25", "DetP50", "DetP75", "DetP90", "DetP99",
                "MonoViolations", "MonoMaxDrop",
                "Transitions", "TransitionPct", "FrozenRunMax", "FrozenRunMean",
                "CorrWithRawRatio", "NotDetMean", "NotDetStd" }, summary);
        AppendHash(hash, dist);
        AppendHash(hash, summary);

        // ---- redundancy: corr(candidate, each of the 4 reconstructable other raw dims) ----
        var redun = new List<object?[]>();
        for (int c = 0; c < CandidateNames.Length; c++)
        {
            int ci = c;
            var line = new object?[OtherDims.Length + 1];
            line[0] = CandidateNames[ci];
            for (int d = 0; d < OtherDims.Length; d++)
            {
                int di = d;
                line[d + 1] = R(Pearson(rows.Select(x => x.Cand[ci]).ToList(), rows.Select(x => x.OtherRaw[di]).ToList()));
            }
            redun.Add(line);
        }
        WriteCsv(Path.Combine(outDir, "lot16_candidate_redundancy.csv"),
            new[] { "Candidate" }.Concat(OtherDims.Select(d => "corr_" + d)).ToArray(), redun);
        AppendHash(hash, redun);

        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== RAW CUSUM RATIO DISTRIBUTION (Scope|Subset|N|Min|Max|Mean|Median|Var|P10|P50|P90|P99) ===");
            foreach (var rr in dist) _output.WriteLine(string.Join(" | ", rr.Select(F)));
            _output.WriteLine("");
            _output.WriteLine("=== CANDIDATE SUMMARY ===");
            _output.WriteLine(string.Join(" | ", new[] { "Candidate", "DetVar", "DetStd", "SatPctDetected", "DetDistinct", "DetP10", "DetP50", "DetP90", "MonoViol", "TransPct", "FrozenRunMax", "CorrRaw" }));
            foreach (var rr in summary)
                _output.WriteLine(string.Join(" | ", new[] { rr[0], rr[14], rr[13], rr[16], rr[17], rr[18], rr[20], rr[22], rr[24], rr[27], rr[28], rr[30] }.Select(F)));
            _output.WriteLine("");
            _output.WriteLine("=== CANDIDATE REDUNDANCY (Pearson vs other raw dims) ===");
            _output.WriteLine(string.Join(" | ", new[] { "Candidate" }.Concat(OtherDims.Select(d => d.ToString()))));
            foreach (var rr in redun) _output.WriteLine(string.Join(" | ", rr.Select(F)));
        }

        foreach (var table in new[] { dist, summary, redun })
            foreach (object?[] rr in table)
                foreach (object? cell in rr)
                    if (cell is double dd) Assert.True(double.IsFinite(dd), $"non-finite cell {dd}");

        return Sha256Hex(hash.ToString());
    }

    // ---- helpers ----
    private static FusionConfidence Get(FusionResult r, FusionDimension d) =>
        r.Dimensions.TryGetValue(d, out FusionConfidence? c) ? c : new FusionConfidence { Value = 0, Confidence = 0, IsAvailable = false };

    private static double R(double x) => double.IsFinite(x) ? Math.Round(x, 8) : x;
    private static double Med(List<double> xs) => xs.Count == 0 ? 0 : Q(xs.OrderBy(v => v).ToList(), .5);
    private static double Q(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        var s = sorted[0] <= sorted[^1] ? sorted : sorted.OrderBy(v => v).ToList();
        double rank = p * (s.Count - 1);
        int lo = (int)Math.Floor(rank), hi = (int)Math.Ceiling(rank);
        return lo == hi ? s[lo] : s[lo] + (rank - lo) * (s[hi] - s[lo]);
    }
    private static double Var(IReadOnlyList<double> xs)
    {
        if (xs.Count < 2) return 0;
        double m = xs.Average();
        return xs.Sum(v => (v - m) * (v - m)) / (xs.Count - 1);
    }
    private static double Std(IReadOnlyList<double> xs) => Math.Sqrt(Var(xs));
    private static int Distinct(List<double> xs) => xs.Select(v => Math.Round(v, 9)).Distinct().Count();
    private static double Pearson(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        int n = Math.Min(a.Count, b.Count);
        if (n < 2) return 0;
        double ma = a.Take(n).Average(), mb = b.Take(n).Average(), sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < n; i++) { double da = a[i] - ma, db = b[i] - mb; sab += da * db; saa += da * da; sbb += db * db; }
        double den = Math.Sqrt(saa * sbb);
        return den <= 1e-15 ? 0 : sab / den;
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
