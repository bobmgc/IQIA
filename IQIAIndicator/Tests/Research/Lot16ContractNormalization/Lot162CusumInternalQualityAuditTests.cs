using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;
using IQIAIndicator.Tests.GoldenDatasets;
using Xunit;

namespace IQIAIndicator.Tests.Research.Lot16ContractNormalization;

/// <summary>
/// LOT 16.2 — CUSUM Measurement-Quality Metric Exposure Audit. OBSERVATION ONLY. No production type
/// modified. Builds a faithful in-test replica of <c>CusumStatistics.Compute</c> (ported verbatim,
/// reusing the real <c>CusumMath</c>), fidelity-gated bit-for-bit against the real
/// <c>CusumValidation.RunOnSeries</c> on every window, and exposes the quantities the real
/// <see cref="CusumResult"/> discards. Candidate measurement-quality metrics are then evaluated for
/// independence from the Lot 16 magnitude signal Strength = r/(1+r), r = peak/threshold.
///
/// Candidates (all parameter-free, per-window, causal):
///   Q1 posNegSymmetry     = max&gt;0 ? 1 - min(|S+|,|S-|)/max(|S+|,|S-|) : 0     (exposed fields only)
///   Q2 baselineDispRatio  = referenceVariance / windowVariance                  (calibration-window representativeness)
///   Q3 detectorAgreement  = min(levelScore,varScore)/max(...)  (0 when variance run degenerate)
///   Q4 seriesKind         = 0.0 level / 1.0 variance                            (nature-of-change label)
///   Q5 relativeThreshold  = threshold / windowMean                             (noise scale, price-normalised)
///   Q6 runLengthFrac      = detected ? (estBreakIdx - calib)/(N - calib) : NaN  (change shape)
///   Q7 estBreakIdxStable  = estBreakIdx == previous window's ? 1 : 0            (Lot 16.1 E, re-tested)
///
/// Dataset: deterministic synthetic price series (SyntheticSeriesCatalog, seeded LCG) sliced with a
/// 30-bar rolling window (production RegimeEngine CusumWindowSize). One optional Yahoo probe; on HTTP
/// 429 / any connectivity issue it STOPS immediately (no retry loop) and proceeds synthetic-only.
/// </summary>
public sealed class Lot162CusumInternalQualityAuditTests
{
    private readonly ITestOutputHelper _output;
    public Lot162CusumInternalQualityAuditTests(ITestOutputHelper output) => _output = output;

    private const int Window = 30;                 // RegimeEngine.CusumWindowSize
    private const double VarianceTolerance = 1e-15; // CusumStatistics.VarianceTolerance

    private static readonly string[] CandidateNames =
        { "Q1_posNegSymmetry", "Q2_baselineDispRatio", "Q3_detectorAgreement", "Q4_seriesKind", "Q5_relativeThreshold", "Q6_runLengthFrac", "Q7_estBreakIdxStable" };

    // ── faithful replica of CusumStatistics.Compute (reuses the real CusumMath) ───────────────────────

    private readonly record struct Run(bool Detected, int EstBreakIdx, double Pos, double Neg, double Threshold, double K, int Kind);

    private sealed record Replica(
        bool Valid, string Reason, bool Detected, int EstBreakIdx, double Pos, double Neg, double Threshold,
        double Confidence, int SampleSize, double RefMean, double RefVar, int CalibrationSize, double K,
        double VarOfSqDev, double LevelScore, double VarScore, bool VarRunDegenerate, int SelectedKind,
        double WindowMean, double WindowVar);

    private static Run RunPageCusum(IReadOnlyList<double> s, int calib, double refMean, double std, int kind)
    {
        int n = s.Count;
        double k = std * Math.Sqrt(2.0 * Math.Log(n) / n) / 2.0;
        double h = std * Math.Sqrt(2.0 * n * Math.Log(n));
        double pos = 0.0, neg = 0.0, maxPos = 0.0, minNeg = 0.0;
        int posCand = calib, negCand = calib;
        bool detected = false; int estIdx = -1;
        for (int i = calib; i < n; i++)
        {
            double dev = s[i] - refMean;
            pos = Math.Max(0.0, pos + dev - k);
            neg = Math.Min(0.0, neg + dev + k);
            if (pos == 0.0) posCand = i + 1;
            if (neg == 0.0) negCand = i + 1;
            maxPos = Math.Max(maxPos, pos);
            minNeg = Math.Min(minNeg, neg);
            if (!detected && pos > h) { detected = true; estIdx = posCand; }
            else if (!detected && Math.Abs(neg) > h) { detected = true; estIdx = negCand; }
        }
        return new Run(detected, estIdx, maxPos, minNeg, h, k, kind);
    }

    private static double Score(Run r) => r.Threshold <= 0.0 ? 0.0 : Math.Max(r.Pos, Math.Abs(r.Neg)) / r.Threshold;

    private static Replica ReplicaCompute(double[] series)
    {
        if (series.Length < 8) return Invalid("court", series.Length);
        foreach (double v in series) if (!double.IsFinite(v)) return Invalid("non finie", series.Length);

        int calib = Math.Max(4, series.Length / 4);
        if (calib >= series.Length) return Invalid("pre-echantillon", series.Length);

        double refMean = CusumMath.Mean(series, calib);
        double refVar = CusumMath.Variance(series, calib, refMean);
        if (!double.IsFinite(refVar) || refVar <= VarianceTolerance) return Invalid("variance nulle", series.Length);

        double std = Math.Sqrt(refVar);
        Run levelRun = RunPageCusum(series, calib, refMean, std, 0);

        double[] sqDev = CusumMath.SquaredDeviations(series, refMean);
        double varOfSqDev = CusumMath.Variance(sqDev, calib, refVar);
        bool varDegenerate = varOfSqDev <= VarianceTolerance;
        Run varianceRun = varDegenerate ? default : RunPageCusum(sqDev, calib, refVar, Math.Sqrt(varOfSqDev), 1);

        double levelScore = Score(levelRun);
        double varScore = varianceRun.Threshold <= 0.0 ? 0.0 : Score(varianceRun);
        Run selected = varScore <= levelScore ? levelRun : varianceRun;
        int selectedKind = varScore <= levelScore ? 0 : 1;

        double peak = Math.Max(selected.Pos, Math.Abs(selected.Neg));
        double confidence = selected.Threshold <= 0.0 ? 0.0 : Math.Clamp(peak / selected.Threshold, 0.0, 1.0);

        double wMean = series.Average();
        double wVar = CusumMath.Variance(series, series.Length, wMean);

        return new Replica(true, "", selected.Detected, selected.EstBreakIdx, selected.Pos, selected.Neg,
            selected.Threshold, confidence, series.Length, refMean, refVar, calib, selected.K, varOfSqDev,
            levelScore, varScore, varDegenerate, selectedKind, wMean, wVar);
    }

    private static Replica Invalid(string reason, int n) =>
        new(false, reason, false, -1, 0, 0, 0, 0, n, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0);

    // ── per-window record ────────────────────────────────────────────────────────────────────────────

    private sealed class Obs
    {
        public required string Series;
        public required int WindowIndex;
        public required bool Detected;
        public required double RawRatio;   // r = peak/threshold
        public required double Strength;   // r/(1+r)  (Lot 16 magnitude reference)
        public required double[] Cand;     // the 7 candidate values (NaN where not applicable)
    }

    [Fact]
    public void Audit_Lot162_CusumInternalQuality_ReplicaFidelity_And_CandidateIndependence()
    {
        // NO NETWORK. Yahoo is rate-limited (HTTP 429) as of this session (Lot 16.1 finding); a probe
        // risks a multi-minute retry/backoff hang (brief §19). This audit runs entirely on deterministic,
        // seeded synthetic price series with KNOWN structural properties - a stronger substrate for a
        // measurement-quality audit than one noisy market pull anyway.
        _output.WriteLine("=== LOT 16.2 — YAHOO: NOT USED (rate-limited; deterministic synthetic-only audit) ===");

        // ── deterministic synthetic price series (long, seeded) ───────────────────────────────────────
        const int len = 2400;
        var seriesSet = new (string Name, double[] Prices)[]
        {
            ("WhiteNoise",        ToD(SyntheticSeriesCatalog.WhiteNoise(len, seed: 42UL))),
            ("RandomWalk",        ToD(SyntheticSeriesCatalog.RandomWalk(len, seed: 42UL))),
            ("Ar1",               ToD(SyntheticSeriesCatalog.Ar1(len, seed: 42UL))),
            ("MeanRevertingOu",   ToD(SyntheticSeriesCatalog.MeanRevertingOu(len, seed: 42UL))),
            ("Trending",          ToD(SyntheticSeriesCatalog.Trending(len, seed: 42UL, drift: 0.0005))),
            ("MeanBreakMid",      ToD(SyntheticSeriesCatalog.StructuralBreak(len, seed: 42UL, shift: 5m))),
            ("VarianceBreakMid",  ToD(SyntheticSeriesCatalog.VarianceBreak(len, seed: 42UL))),
            ("HighVolatility",    ToD(SyntheticSeriesCatalog.HighVolatility(len, seed: 42UL))),
            ("LowVolatility",     ToD(SyntheticSeriesCatalog.LowVolatility(len, seed: 42UL))),
        };

        var observations = new List<Obs>(seriesSet.Length * len);
        int fidelityChecks = 0, fidelityMismatches = 0, validWindows = 0, varRunDegenerateCount = 0;
        var firstMismatches = new List<string>();

        foreach ((string name, double[] prices) in seriesSet)
        {
            int prevEstBreakIdx = int.MinValue;
            for (int start = 0; start + Window <= prices.Length; start++)
            {
                var window = new double[Window];
                Array.Copy(prices, start, window, 0, Window);

                Replica rep = ReplicaCompute(window);
                CusumResult real = CusumValidation.RunOnSeries(window);

                // ── FIDELITY GATE: replica must reproduce the real result bit-for-bit ──────────────────
                fidelityChecks++;
                bool ok = rep.Valid == real.IsValid;
                if (ok && real.IsValid)
                {
                    ok = Bits(rep.Pos) == Bits(real.PositiveCusum)
                      && Bits(rep.Neg) == Bits(real.NegativeCusum)
                      && Bits(rep.Threshold) == Bits(real.Threshold)
                      && Bits(rep.Confidence) == Bits(real.Confidence)
                      && rep.Detected == real.ChangeDetected
                      && rep.EstBreakIdx == real.EstimatedBreakIndex
                      && rep.SampleSize == real.SampleSize;
                }
                if (!ok)
                {
                    fidelityMismatches++;
                    if (firstMismatches.Count < 8)
                        firstMismatches.Add($"{name}#{start}: replica(valid={rep.Valid},pos={rep.Pos:G17},neg={rep.Neg:G17},h={rep.Threshold:G17},conf={rep.Confidence:G17},det={rep.Detected},idx={rep.EstBreakIdx}) vs real(valid={real.IsValid},pos={real.PositiveCusum:G17},neg={real.NegativeCusum:G17},h={real.Threshold:G17},conf={real.Confidence:G17},det={real.ChangeDetected},idx={real.EstimatedBreakIndex})");
                    prevEstBreakIdx = rep.Valid ? rep.EstBreakIdx : prevEstBreakIdx;
                    continue;
                }
                if (!real.IsValid) { prevEstBreakIdx = int.MinValue; continue; }

                // cross-check the replica's kind selection against the real Explanation label
                bool realVarianceKind = real.Explanation.Contains("variance", StringComparison.OrdinalIgnoreCase);
                Assert.Equal(realVarianceKind, rep.SelectedKind == 1);

                validWindows++;
                if (rep.VarRunDegenerate) varRunDegenerateCount++;

                double peak = Math.Max(rep.Pos, Math.Abs(rep.Neg));
                double r = rep.Threshold > 0.0 ? peak / rep.Threshold : 0.0;
                double strength = (!double.IsFinite(r) || r <= 0.0) ? 0.0 : r / (1.0 + r);

                double absPos = Math.Abs(rep.Pos), absNeg = Math.Abs(rep.Neg);
                double maxSide = Math.Max(absPos, absNeg), minSide = Math.Min(absPos, absNeg);
                double q1 = maxSide > 0.0 ? 1.0 - minSide / maxSide : 0.0;
                double q2 = rep.WindowVar > 0.0 ? Finite(rep.RefVar / rep.WindowVar) : double.NaN;
                double q3 = rep.VarRunDegenerate
                    ? 0.0
                    : (Math.Max(rep.LevelScore, rep.VarScore) > 0.0 ? Math.Min(rep.LevelScore, rep.VarScore) / Math.Max(rep.LevelScore, rep.VarScore) : 0.0);
                double q4 = rep.SelectedKind; // 0 level, 1 variance
                double q5 = rep.WindowMean != 0.0 ? Finite(rep.Threshold / rep.WindowMean) : double.NaN;
                double q6 = rep.Detected && rep.EstBreakIdx >= 0
                    ? Math.Clamp((rep.EstBreakIdx - rep.CalibrationSize) / (double)Math.Max(1, rep.SampleSize - rep.CalibrationSize), 0.0, 1.0)
                    : double.NaN;
                double q7 = prevEstBreakIdx == int.MinValue ? double.NaN : (rep.EstBreakIdx == prevEstBreakIdx ? 1.0 : 0.0);
                prevEstBreakIdx = rep.EstBreakIdx;

                observations.Add(new Obs
                {
                    Series = name, WindowIndex = start, Detected = rep.Detected,
                    RawRatio = r, Strength = strength,
                    Cand = new[] { q1, q2, q3, q4, q5, q6, q7 }
                });
            }
        }

        _output.WriteLine($"FidelityChecks={fidelityChecks}, Mismatches={fidelityMismatches}, ValidWindows={validWindows}, VarianceRunDegenerate={varRunDegenerateCount} ({Pct(varRunDegenerateCount, validWindows):F2}%)");
        foreach (string m in firstMismatches) _output.WriteLine("  MISMATCH " + m);
        Assert.Equal(0, fidelityMismatches); // the replica MUST be a faithful mirror before its internals can be trusted
        Assert.True(validWindows > 3000, $"expected a large valid-window sample, got {validWindows}");

        string outDir = ResolveOutputDirectory();
        string h1 = Aggregate(observations, validWindows, varRunDegenerateCount, outDir, print: true);
        string h2 = Aggregate(observations, validWindows, varRunDegenerateCount, outDir, print: false);
        _output.WriteLine("");
        _output.WriteLine($"=== DETERMINISM === Hash1={h1} Hash2={h2} Identical={h1 == h2}");
        Assert.Equal(h1, h2);
    }

    private string Aggregate(List<Obs> obs, int validWindows, int varRunDegenerate, string outDir, bool print)
    {
        var hash = new StringBuilder();
        var rows = new List<object?[]>();

        List<double> strengthAll = obs.Select(o => o.Strength).ToList();
        List<double> ratioAll = obs.Select(o => o.RawRatio).ToList();
        List<double> ratioRank = Rank(ratioAll);
        List<double> strengthRank = Rank(strengthAll);

        for (int c = 0; c < CandidateNames.Length; c++)
        {
            int ci = c;
            List<double> paired = obs.Select(o => o.Cand[ci]).ToList();
            // finite-only view (Q2/Q5/Q6/Q7 carry NaN where not applicable)
            var finiteIdx = Enumerable.Range(0, paired.Count).Where(i => double.IsFinite(paired[i])).ToList();
            List<double> v = finiteIdx.Select(i => paired[i]).ToList();
            if (v.Count < 2) { rows.Add(new object?[] { CandidateNames[ci], 0, "NO_FINITE_DATA" }); continue; }

            List<double> vStrength = finiteIdx.Select(i => strengthAll[i]).ToList();
            List<double> vRatio = finiteIdx.Select(i => ratioAll[i]).ToList();

            double mean = v.Average();
            double var = v.Count > 1 ? v.Sum(x => (x - mean) * (x - mean)) / (v.Count - 1) : 0.0;
            int distinct = v.Select(x => Math.Round(x, 9)).Distinct().Count();
            int sat1 = v.Count(x => x >= 0.9999);
            int sat0 = v.Count(x => Math.Abs(x) <= 1e-9);
            int narrowBand = v.Count(x => x >= 0.45 && x <= 0.55);
            int transitions = 0;
            for (int i = 1; i < v.Count; i++) if (Math.Abs(v[i] - v[i - 1]) > 1e-9) transitions++;
            int exactEqStrength = finiteIdx.Count(i => Bits(paired[i]) == Bits(strengthAll[i]));

            double pearsonStrength = Pearson(v, vStrength);
            double spearmanStrength = Pearson(Rank(v), Rank(vStrength));
            double spearmanRatio = Pearson(Rank(v), Rank(vRatio));   // Control 3: collapse to monotone transform of magnitude?

            rows.Add(new object?[]
            {
                CandidateNames[ci], v.Count, R(v.Min()), R(v.Max()), R(mean), R(Median(v)), R(Math.Sqrt(var)), R(var),
                distinct, R(Pct(sat1, v.Count)), R(Pct(sat0, v.Count)), R(Pct(narrowBand, v.Count)),
                transitions, R(Pct(transitions, Math.Max(1, v.Count - 1))),
                R(Pct(exactEqStrength, v.Count)),
                R(pearsonStrength), R(spearmanStrength), R(spearmanRatio)
            });
        }

        WriteCsv(Path.Combine(outDir, "lot162_cusum_quality_candidates.csv"),
            new[] { "Candidate", "N", "Min", "Max", "Mean", "Median", "Std", "Var", "Distinct",
                "SatPct(>=0.9999)", "ZeroPct", "NarrowBand[.45,.55]Pct", "Transitions", "TransitionPct",
                "ExactEqStrengthPct", "PearsonWithStrength", "SpearmanWithStrength", "SpearmanWithRawRatio" }, rows);
        AppendHash(hash, rows);

        var meta = new List<object?[]>
        {
            new object?[] { "ValidWindows", validWindows },
            new object?[] { "VarianceRunDegeneratePct", R(Pct(varRunDegenerate, validWindows)) },
            new object?[] { "DetectedPct", R(Pct(obs.Count(o => o.Detected), obs.Count)) },
            new object?[] { "Strength_mean", R(strengthAll.Average()) },
            new object?[] { "Strength_var", R(strengthAll.Count > 1 ? strengthAll.Sum(x => (x - strengthAll.Average()) * (x - strengthAll.Average())) / (strengthAll.Count - 1) : 0.0) },
        };
        WriteCsv(Path.Combine(outDir, "lot162_meta.csv"), new[] { "Key", "Value" }, meta);
        AppendHash(hash, meta);

        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== LOT 16.2 CANDIDATE QUALITY METRICS vs Strength = r/(1+r) ===");
            _output.WriteLine("Candidate | N | Min | Max | Mean | Median | Std | Var | Distinct | Sat>=1% | Zero% | Band% | Trans% | ExactEqStrength% | Pearson(Strength) | Spearman(Strength) | Spearman(rawRatio)");
            foreach (object?[] rr in rows)
                _output.WriteLine(string.Join(" | ", rr.Select(F)));
            _output.WriteLine("");
            foreach (object?[] rr in meta) _output.WriteLine(string.Join(" = ", rr.Select(F)));
        }

        foreach (object?[] row in rows.Concat(meta))
            foreach (object? cell in row)
                if (cell is double d) Assert.True(double.IsFinite(d), $"non-finite cell {d}");

        return Sha256Hex(hash.ToString());
    }

    // ── helpers ──
    private static double[] ToD(decimal[] xs) => Array.ConvertAll(xs, x => (double)x);
    private static double Finite(double x) => double.IsFinite(x) ? x : double.NaN;
    private static long Bits(double d) => BitConverter.DoubleToInt64Bits(d);
    private static double R(double x) => double.IsFinite(x) ? Math.Round(x, 8) : x;
    private static double Pct(int c, int t) => t > 0 ? 100.0 * c / t : 0.0;
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
    private static List<double> Rank(IReadOnlyList<double> xs)
    {
        var idx = Enumerable.Range(0, xs.Count).OrderBy(i => xs[i]).ToList();
        var ranks = new double[xs.Count];
        int i = 0;
        while (i < idx.Count)
        {
            int j = i;
            while (j + 1 < idx.Count && xs[idx[j + 1]] == xs[idx[i]]) j++;
            double avg = (i + j) / 2.0;
            for (int k = i; k <= j; k++) ranks[idx[k]] = avg;
            i = j + 1;
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
