using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace IQIAIndicator.Tests.Research.OrderFlowFeasibility;

/// <summary>
/// QDE-017 — READ-ONLY. Does per-bar order-flow state (Delta / footprint-derived features) at bar close
/// predict the next-H-bar MES return, net of a realistic round-trip cost? No production type modified.
/// Consumes the CSV produced by the throwaway ATAS indicator <c>OrderFlowExport</c> (via
/// <see cref="OrderFlowCsvBarSource"/>). Methodology mirrors QDE-014: tercile segmentation, 95% CI,
/// purged train/OOS split. Skips cleanly until the CSV exists.
/// </summary>
public sealed class OrderFlowConditioningPilotTests
{
    private const double RoundTripCostPts = 0.77;      // MES base round-trip, same as QDE-012+/QDE-014
    private static readonly int[] Horizons = { 1, 3, 6, 12 };
    private const double TrainFraction = 0.60;
    private const int PurgeBars = 20;

    private readonly ITestOutputHelper _output;
    private readonly List<string> _log = new();
    public OrderFlowConditioningPilotTests(ITestOutputHelper output) => _output = output;
    private void W(string s) { _output.WriteLine(s); _log.Add(s); }

    [Fact]
    public void OrderFlow_Conditions_MES_Forward_Return()
    {
        string? path = OrderFlowCsvBarSource.FindLatest();
        Assert.SkipUnless(path is not null && File.Exists(path),
            "No orderflow_export_*.csv under %LOCALAPPDATA%\\IQIA\\orderflow - run the 'OrderFlowExport' " +
            "ATAS indicator on a MES M5 real-feed chart first (QDE-017).");

        IReadOnlyList<OrderFlowBar> raw = OrderFlowCsvBarSource.ReadRaw(path!);
        IReadOnlyList<OrderFlowBar> rows = OrderFlowCsvBarSource.ReadClean(path!);
        W($"CSV: {path}");
        W($"raw rows={raw.Count}  clean rows={rows.Count}  " +
          $"({rows[0].Timestamp:yyyy-MM-dd}..{rows[^1].Timestamp:yyyy-MM-dd})");

        // ── data-quality re-check across the FULL corpus (QDE-016 spot-checked only 6 bars) ──────
        int bidAskEqVol = 0, deltaOk = 0, betweensZero = 0, oiZero = 0;
        foreach (OrderFlowBar r in rows)
        {
            if (Math.Abs((r.Bid + r.Ask) - r.Volume) <= 0.5m) bidAskEqVol++;
            if (Math.Abs((r.Ask - r.Bid) - r.Delta) <= 0.5m) deltaOk++;
            if (r.Betweens == 0m) betweensZero++;
            if (r.OI == 0m) oiZero++;
        }
        int nAll = rows.Count;
        W($"data quality (n={nAll}): bid+ask==volume {100.0 * bidAskEqVol / nAll:0.0}%  " +
          $"delta==ask-bid {100.0 * deltaOk / nAll:0.0}%  betweens==0 {100.0 * betweensZero / nAll:0.0}%  " +
          $"oi==0 {100.0 * oiZero / nAll:0.0}%");

        // bar interval (modal gap) for gap-aware forward windows
        var gaps = new List<double>();
        for (int i = 1; i < rows.Count; i++) gaps.Add((rows[i].Timestamp - rows[i - 1].Timestamp).TotalMinutes);
        gaps.Sort();
        double interval = gaps[gaps.Count / 2];
        int contiguous = gaps.Count(g => g <= interval * 1.5);
        W($"modal bar interval = {interval:0.#} min  ;  contiguous steps {100.0 * contiguous / gaps.Count:0.0}%  " +
          $"(gaps = weekends / CME break / holidays, as QDE-016 Phase 0)");

        double[] close = rows.Select(r => (double)r.Close).ToArray();
        long[] ts = rows.Select(r => r.Timestamp.Ticks).ToArray();

        // ── per-bar order-flow features (all causal, at bar i close) ────────────────────────────
        double[] deltaRatio = new double[nAll];      // signed pressure, [-1,1]
        double[] absDeltaRatio = new double[nAll];   // |pressure|
        double[] deltaRange = new double[nAll];      // intrabar delta excursion / volume
        double[] betweensRatio = new double[nAll];
        for (int i = 0; i < nAll; i++)
        {
            double v = (double)rows[i].Volume;
            deltaRatio[i] = v > 0 ? (double)rows[i].Delta / v : 0.0;
            absDeltaRatio[i] = Math.Abs(deltaRatio[i]);
            deltaRange[i] = v > 0 ? (double)(rows[i].MaxDelta - rows[i].MinDelta) / v : 0.0;
            betweensRatio[i] = v > 0 ? (double)rows[i].Betweens / v : 0.0;
        }

        int split = (int)(nAll * TrainFraction);

        foreach (int h in Horizons)
        {
            long maxSpan = (long)(interval * h * 1.6 * TimeSpan.TicksPerMinute);
            var idx = new List<int>();
            for (int i = 0; i + h < nAll; i++)
                if (ts[i + h] - ts[i] <= maxSpan) idx.Add(i);   // no session break inside the window

            double[] fwd = idx.Select(i => close[i + h] - close[i]).ToArray();   // forward move in points
            double meanFwd = fwd.Average();
            double meanAbs = fwd.Select(Math.Abs).Average();

            W("");
            W($"================  HORIZON H={h} bars ({interval * h:0} min)  —  n={idx.Count}  ================");
            W($"unconditional: mean fwdMove={meanFwd:+0.000;-0.000} pt   mean |fwdMove|={meanAbs:0.000} pt   " +
              $"round-trip cost={RoundTripCostPts} pt");

            // ---- TEST 1: directional rule "go with sign(Delta)" ----
            var betPts = new List<double>(idx.Count);
            var betBar = new List<int>(idx.Count);
            int hits = 0;
            for (int k = 0; k < idx.Count; k++)
            {
                int i = idx[k];
                double dir = Math.Sign(deltaRatio[i]);
                if (dir == 0) continue;
                double signed = dir * fwd[k];
                betPts.Add(signed);
                betBar.Add(i);
                if (signed > 0) hits++;
            }
            (double gm, double gci) = MeanCi(betPts);
            (double nm, double nci) = MeanCi(betPts.Select(x => x - RoundTripCostPts));
            double hitRate = betPts.Count > 0 ? hits / (double)betPts.Count : double.NaN;
            (double trm, double trci) = MeanCi(Zip(betBar, betPts).Where(t => t.b < split - PurgeBars).Select(t => t.p - RoundTripCostPts));
            (double oom, double ooci) = MeanCi(Zip(betBar, betPts).Where(t => t.b >= split).Select(t => t.p - RoundTripCostPts));
            W($"  TEST1 go-with-Delta: n={betPts.Count}  hitRate={hitRate:0.000} (vs 0.500)  " +
              $"gross={gm:+0.000;-0.000}+/-{gci:0.000} pt   NET={nm:+0.000;-0.000}+/-{nci:0.000} pt   " +
              $"[{Verdict(nm, nci)}]");
            W($"        TRAIN net={trm:+0.000;-0.000}+/-{trci:0.000}   OOS net={oom:+0.000;-0.000}+/-{ooci:0.000}");

            // ---- TEST 2: mean forward move by tercile of each feature ----
            TercileTable("deltaRatio (signed)", deltaRatio, idx, fwd, split, signedTarget: true);
            TercileTable("absDeltaRatio", absDeltaRatio, idx, fwd, split, signedTarget: false);
            TercileTable("deltaRange (excursion/vol)", deltaRange, idx, fwd, split, signedTarget: false);
            TercileTable("betweensRatio", betweensRatio, idx, fwd, split, signedTarget: false);

            // ---- TEST 3: Pearson corr(feature, forward move) ----
            double band = 1.96 / Math.Sqrt(idx.Count);
            W($"  TEST3 Pearson(feature, fwdMove)  [white-noise band +/-{band:0.0000}]:");
            W($"        deltaRatio    r={Pearson(idx.Select(i => deltaRatio[i]), fwd):+0.0000}");
            W($"        absDeltaRatio r={Pearson(idx.Select(i => absDeltaRatio[i]), fwd.Select(Math.Abs)):+0.0000}  (vs |fwdMove|)");
            W($"        deltaRange    r={Pearson(idx.Select(i => deltaRange[i]), fwd.Select(Math.Abs)):+0.0000}  (vs |fwdMove|)");
        }

        WriteReport();
        Assert.True(rows.Count > 500, $"expected >500 clean bars, got {rows.Count}");
    }

    private void TercileTable(string name, double[] feature, List<int> idx, double[] fwd, int split, bool signedTarget)
    {
        var pairs = new (double f, double y, int bar)[idx.Count];
        for (int k = 0; k < idx.Count; k++)
        {
            int i = idx[k];
            pairs[k] = (feature[i], signedTarget ? fwd[k] : Math.Abs(fwd[k]), i);
        }
        var sorted = pairs.OrderBy(p => p.f).ToArray();
        double lo = sorted[sorted.Length / 3].f, hi = sorted[2 * sorted.Length / 3].f;
        W($"  TEST2 {name}: terciles at {lo:+0.0000;-0.0000} / {hi:+0.0000;-0.0000}   ({(signedTarget ? "target = signed fwdMove pt" : "target = |fwdMove| pt")})");
        foreach ((string lbl, Func<double, bool> sel) in new (string, Func<double, bool>)[]
        {
            ("low ", f => f <= lo), ("mid ", f => f > lo && f <= hi), ("high", f => f > hi)
        })
        {
            var seg = pairs.Where(p => sel(p.f)).ToArray();
            if (seg.Length == 0) { W($"        {lbl} n=0"); continue; }
            (double m, double ci) = MeanCi(seg.Select(p => p.y));
            (double trm, double trci) = MeanCi(seg.Where(p => p.bar < split - PurgeBars).Select(p => p.y));
            (double oom, double ooci) = MeanCi(seg.Where(p => p.bar >= split).Select(p => p.y));
            W($"        {lbl} n={seg.Length,6}  mean={m:+0.000;-0.000}+/-{ci:0.000} pt   " +
              $"TRAIN {trm:+0.000;-0.000}+/-{trci:0.000}  OOS {oom:+0.000;-0.000}+/-{ooci:0.000}");
        }
    }

    private static string Verdict(double m, double ci) =>
        m - ci > 0 ? "NET+ ROBUST" : m + ci < 0 ? "net- robust" : "straddles 0";

    private static IEnumerable<(int b, double p)> Zip(List<int> bars, List<double> pts)
    {
        for (int i = 0; i < bars.Count; i++) yield return (bars[i], pts[i]);
    }

    private static (double mean, double ci95) MeanCi(IEnumerable<double> xs)
    {
        double[] v = xs.ToArray();
        if (v.Length == 0) return (double.NaN, double.NaN);
        double m = v.Average();
        if (v.Length < 2) return (m, double.NaN);
        double sd = Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / (v.Length - 1));
        return (m, 1.96 * sd / Math.Sqrt(v.Length));
    }

    private static double Pearson(IEnumerable<double> aa, IEnumerable<double> bb)
    {
        double[] a = aa.ToArray(), b = bb.ToArray();
        int n = Math.Min(a.Length, b.Length);
        if (n < 2) return 0.0;
        double ma = a.Take(n).Average(), mb = b.Take(n).Average();
        double sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < n; i++) { double da = a[i] - ma, db = b[i] - mb; sab += da * db; saa += da * da; sbb += db * db; }
        double d = Math.Sqrt(saa * sbb);
        return d <= 1e-15 ? 0.0 : sab / d;
    }

    private void WriteReport()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) return;
        string od = Path.Combine(dir, "Research", "OrderFlowFeasibility", "Output");
        Directory.CreateDirectory(od);
        File.WriteAllText(Path.Combine(od, "orderflow_conditioning_pilot.txt"), string.Join("\n", _log) + "\n");
    }
}
