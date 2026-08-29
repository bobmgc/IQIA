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
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Risk;
using Xunit;
using FusionEngine = IQIAIndicator.Engine.Fusion.EvidenceFusionEngine;
using IQIAIndicator.Tests.BacktestTests.Yahoo;

namespace IQIAIndicator.Tests.Research.SixDimensionAudit;

/// <summary>
/// INDEPENDENT SIX-DIMENSION FUSION AUDIT - observation only, additive, deterministic. Does NOT modify any
/// production type. Runs the unmodified <see cref="BacktestEngine.RunSignalPipeline"/> over the real Yahoo
/// MES M5 dataset, then reconstructs (raw) and (stabilized) fusion state bar-by-bar with a
/// production-equivalent rule list (same rules, same order as IQIAIndicator.cs), and dumps per-dimension
/// Value/Confidence distributions, saturation rates, Value-Confidence correlation, raw-vs-stable temporal
/// freeze, and the 6x6 inter-dimension correlation matrix.
/// </summary>
public sealed class SixDimensionIndependentAuditTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public SixDimensionIndependentAuditTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static readonly FusionDimension[] Dims =
    {
        FusionDimension.Stationarity, FusionDimension.Persistence, FusionDimension.MeanReversion,
        FusionDimension.StructuralStability, FusionDimension.RandomWalk, FusionDimension.StructuralBreak
    };

    private static FusionEngine BuildProductionEquivalentFusionEngine() => new(new IFusionRule[]
    {
        new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new RandomWalkRule(),
        new StructuralBreakEvidenceRule()
    });

    private sealed class Row
    {
        public required int BarIndex;
        public required double[] RawValue;
        public required double[] RawConf;
        public required bool[] RawAvail;
        public required double[] StableValue;
        public required double[] StableConf;
    }

    [Fact]
    public void Integration_Network_SixDimensionIndependentAudit()
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
                series, new BacktestWindow("SIXDIM-FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            BacktestSignalPipelineResult signalResult = new BacktestEngine().RunSignalPipeline(scenario, warmupBars);
            Assert.Equal(0, signalResult.ExceptionCount);
            _output.WriteLine($"BarsProcessed={signalResult.BarsProcessed}, ReadyBars={signalResult.ReadyBars}, WarmupBars={signalResult.WarmupBars}, TotalSeriesBars={series.Count}");

            FusionEngine fusionEngine = BuildProductionEquivalentFusionEngine();
            var fusionState = new FusionStateManager();
            var rows = new List<Row>(series.Count);

            foreach (BacktestSignalResult bar in signalResult.Bars)
            {
                if (bar.Status is BacktestSignalStatus.Rejected or BacktestSignalStatus.Exception) continue;
                if (bar.Regime is null) continue;

                FusionResult raw = fusionEngine.Fuse(new FusionContext
                {
                    Evidence = bar.Regime, Timestamp = bar.Timestamp, Symbol = series.Symbol,
                    TimeFrame = series.TimeFrame, EvaluationId = Guid.Empty
                });
                FusionSnapshot snap = fusionState.Update(raw, bar.Timestamp);

                if (bar.Status != BacktestSignalStatus.Ready) continue;

                var rv = new double[Dims.Length];
                var rc = new double[Dims.Length];
                var ra = new bool[Dims.Length];
                var sv = new double[Dims.Length];
                var sc = new double[Dims.Length];
                for (int d = 0; d < Dims.Length; d++)
                {
                    FusionConfidence rawC = Get(raw, Dims[d]);
                    FusionConfidence stbC = Get(snap.StableResult, Dims[d]);
                    rv[d] = rawC.Value; rc[d] = rawC.Confidence; ra[d] = rawC.IsAvailable;
                    sv[d] = stbC.Value; sc[d] = stbC.Confidence;
                }

                rows.Add(new Row
                {
                    BarIndex = bar.BarIndex,
                    RawValue = rv, RawConf = rc, RawAvail = ra, StableValue = sv, StableConf = sc
                });
            }

            _output.WriteLine($"ObservedReadyBars={rows.Count}");
            Assert.Equal(signalResult.ReadyBars, rows.Count);

            string outDir = ResolveOutputDirectory();
            string h1 = Aggregate(rows, series.Count, outDir, print: true);
            string h2 = Aggregate(rows, series.Count, outDir, print: false);
            _output.WriteLine($"=== DETERMINISM === Hash1={h1} Hash2={h2} Identical={h1 == h2}");
            Assert.Equal(h1, h2);
        }
        catch (Exception exception) when (exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private string Aggregate(List<Row> rows, int totalSeriesBars, string outDir, bool print)
    {
        var hash = new StringBuilder();
        var dist = new List<object?[]>();

        for (int d = 0; d < Dims.Length; d++)
        {
            int di = d;
            foreach ((string stage, Func<Row, double> val, Func<Row, double> conf, Func<Row, bool>? avail) in
                     new (string, Func<Row, double>, Func<Row, double>, Func<Row, bool>?)[]
                     {
                         ("RAW", r => r.RawValue[di], r => r.RawConf[di], r => r.RawAvail[di]),
                         ("STABLE", r => r.StableValue[di], r => r.StableConf[di], null)
                     })
            {
                List<double> v = rows.Select(val).ToList();
                List<double> c = rows.Select(conf).ToList();
                int n = v.Count;
                int availN = avail is null ? n : rows.Count(avail);
                dist.Add(new object?[]
                {
                    Dims[di], stage, "Value", n, availN,
                    R(v.Min()), R(v.Max()), R(v.Average()), R(Median(v)), R(StdDev(v)),
                    Distinct(v), LongestRun(v), Pct(v.Count(x => x <= 1e-9), n),
                    Pct(v.Count(x => x >= 1.0 - 1e-9), n), Pct(v.Count(x => x >= 0.45 && x <= 0.55), n),
                    R(Pearson(v, c))
                });
                dist.Add(new object?[]
                {
                    Dims[di], stage, "Confidence", n, availN,
                    R(c.Min()), R(c.Max()), R(c.Average()), R(Median(c)), R(StdDev(c)),
                    Distinct(c), LongestRun(c), Pct(c.Count(x => x <= 1e-9), n),
                    Pct(c.Count(x => x >= 1.0 - 1e-9), n), Pct(c.Count(x => x >= 0.45 && x <= 0.55), n),
                    null
                });
            }
        }

        WriteCsv(Path.Combine(outDir, "sixdim_distribution.csv"),
            new[] { "Dimension", "Stage", "Field", "N", "AvailableN", "Min", "Max", "Mean", "Median", "StdDev",
                "DistinctCount", "LongestIdenticalRun", "PctEq0", "PctEq1", "PctIn[0.45,0.55]", "Corr(Value,Confidence)" },
            dist);
        AppendHash(hash, dist);

        var freeze = new List<object?[]>();
        for (int d = 0; d < Dims.Length; d++)
        {
            int di = d;
            int rawChangedStableFrozen = 0, bothChanged = 0, bothFrozen = 0, rawFrozenStableChanged = 0;
            for (int i = 1; i < rows.Count; i++)
            {
                bool rc = Math.Abs(rows[i].RawValue[di] - rows[i - 1].RawValue[di]) > 1e-9;
                bool sc = Math.Abs(rows[i].StableValue[di] - rows[i - 1].StableValue[di]) > 1e-9;
                if (rc && !sc) rawChangedStableFrozen++;
                else if (rc && sc) bothChanged++;
                else if (!rc && !sc) bothFrozen++;
                else rawFrozenStableChanged++;
            }
            int pairs = Math.Max(1, rows.Count - 1);
            freeze.Add(new object?[]
            {
                Dims[di], rows.Count,
                R(StdDev(rows.Select(r => r.RawValue[di]).ToList())),
                R(StdDev(rows.Select(r => r.StableValue[di]).ToList())),
                rawChangedStableFrozen, bothChanged, bothFrozen, rawFrozenStableChanged,
                Pct(rawChangedStableFrozen, pairs),
                R(MeanFrozenRun(rows.Select(r => r.StableValue[di]).ToList())),
                MaxFrozenRun(rows.Select(r => r.StableValue[di]).ToList())
            });
        }
        WriteCsv(Path.Combine(outDir, "sixdim_raw_vs_stable_freeze.csv"),
            new[] { "Dimension", "N", "RawStdDev", "StableStdDev", "RawChangedStableFrozen", "BothChanged",
                "BothFrozen", "RawFrozenStableChanged", "PctRawChangedStableFrozen", "MeanStableFrozenRun", "MaxStableFrozenRun" },
            freeze);
        AppendHash(hash, freeze);

        foreach ((string stage, Func<Row, int, double> pick) in new (string, Func<Row, int, double>)[]
                 {
                     ("RAW", (r, k) => r.RawValue[k]),
                     ("STABLE", (r, k) => r.StableValue[k])
                 })
        {
            var matrix = new List<object?[]>();
            for (int a = 0; a < Dims.Length; a++)
            {
                var line = new object?[Dims.Length + 2];
                line[0] = stage;
                line[1] = Dims[a];
                List<double> va = rows.Select(r => pick(r, a)).ToList();
                for (int b = 0; b < Dims.Length; b++)
                {
                    List<double> vb = rows.Select(r => pick(r, b)).ToList();
                    line[b + 2] = R(Pearson(va, vb));
                }
                matrix.Add(line);
            }
            WriteCsv(Path.Combine(outDir, $"sixdim_correlation_{stage.ToLowerInvariant()}.csv"),
                new[] { "Stage", "Dimension" }.Concat(Dims.Select(x => x.ToString())).ToArray(), matrix);
            AppendHash(hash, matrix);

            if (print)
            {
                _output.WriteLine("");
                _output.WriteLine($"=== {stage} inter-dimension Pearson (Value) ===");
                foreach (object?[] r in matrix) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
            }
        }

        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== sixdim_distribution ===");
            foreach (object?[] r in dist) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
            _output.WriteLine("");
            _output.WriteLine("=== sixdim_raw_vs_stable_freeze ===");
            foreach (object?[] r in freeze) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        foreach (object?[] row in dist.Concat(freeze))
            foreach (object? cell in row)
                if (cell is double dd) Assert.True(double.IsFinite(dd), $"non-finite cell {dd}");

        return Sha256Hex(hash.ToString());
    }

    private static FusionConfidence Get(FusionResult r, FusionDimension dim) =>
        r.Dimensions.TryGetValue(dim, out FusionConfidence? c)
            ? c
            : new FusionConfidence { Value = 0.0, Confidence = 0.0, Explanation = "Absent", IsAvailable = false };

    private static double R(double x) => Math.Round(x, 6);
    private static double Pct(int c, int t) => t > 0 ? Math.Round(100.0 * c / t, 3) : 0.0;

    private static double Median(List<double> xs)
    {
        if (xs.Count == 0) return 0.0;
        var s = xs.OrderBy(v => v).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : 0.5 * (s[s.Count / 2 - 1] + s[s.Count / 2]);
    }

    private static double StdDev(IReadOnlyList<double> xs)
    {
        if (xs.Count < 2) return 0.0;
        double m = xs.Average();
        return Math.Sqrt(xs.Sum(v => (v - m) * (v - m)) / (xs.Count - 1));
    }

    private static int Distinct(List<double> xs) => xs.Select(v => Math.Round(v, 9)).Distinct().Count();

    private static int LongestRun(List<double> xs)
    {
        int best = 0, cur = 0;
        for (int i = 0; i < xs.Count; i++)
        {
            if (i > 0 && Math.Abs(xs[i] - xs[i - 1]) <= 1e-9) cur++;
            else cur = 1;
            best = Math.Max(best, cur);
        }
        return best;
    }

    private static double MeanFrozenRun(List<double> xs)
    {
        var runs = new List<int>();
        int cur = 1;
        for (int i = 1; i < xs.Count; i++)
        {
            if (Math.Abs(xs[i] - xs[i - 1]) <= 1e-9) cur++;
            else { runs.Add(cur); cur = 1; }
        }
        runs.Add(cur);
        return runs.Count == 0 ? 0.0 : runs.Average();
    }

    private static int MaxFrozenRun(List<double> xs)
    {
        int best = 1, cur = 1;
        for (int i = 1; i < xs.Count; i++)
        {
            if (Math.Abs(xs[i] - xs[i - 1]) <= 1e-9) cur++;
            else cur = 1;
            best = Math.Max(best, cur);
        }
        return xs.Count == 0 ? 0 : best;
    }

    private static double Pearson(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        int n = Math.Min(a.Count, b.Count);
        if (n < 2) return 0.0;
        double ma = a.Take(n).Average(), mb = b.Take(n).Average();
        double sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < n; i++)
        {
            double da = a[i] - ma, db = b[i] - mb;
            sab += da * db; saa += da * da; sbb += db * db;
        }
        double denom = Math.Sqrt(saa * sbb);
        return denom <= 1e-15 ? 0.0 : sab / denom;
    }

    private static string ResolveOutputDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null)
            throw new InvalidOperationException("Could not locate IQIAIndicator.Tests.csproj.");
        string outDir = Path.Combine(dir, "Research", "SixDimensionAudit", "Output");
        Directory.CreateDirectory(outDir);
        return outDir;
    }

    private static void WriteCsv(string path, string[] headers, List<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(CsvCell)));
        foreach (object?[] row in rows)
        {
            var padded = new object?[headers.Length];
            for (int i = 0; i < headers.Length; i++) padded[i] = i < row.Length ? row[i] : null;
            sb.AppendLine(string.Join(",", padded.Select(CsvCell)));
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static string CsvCell(object? value)
    {
        string s = FormatCell(value);
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n')) s = "\"" + s.Replace("\"", "\"\"") + "\"";
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
        foreach (object?[] row in rows) sb.Append(string.Join("|", row.Select(FormatCell))).Append(';');
    }

    private static string Sha256Hex(string s)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
