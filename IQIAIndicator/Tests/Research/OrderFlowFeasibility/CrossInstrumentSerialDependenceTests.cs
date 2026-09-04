using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;
using Xunit;

namespace IQIAIndicator.Tests.Research.OrderFlowFeasibility;

/// <summary>
/// QDE-020 — READ-ONLY cross-instrument screening of serial dependence. Reproduces the QDE-012 §2.h
/// methodology (ACF + Bartlett CI, Lo-MacKinlay variance ratio via the unchanged production
/// <see cref="VarianceRatioStatistics"/>, Wald-Wolfowitz runs test) on H1 native ~700d for a battery of
/// futures spanning genuinely different asset classes, then applies a Holm multiple-comparison correction
/// across instruments. No production type modified; YahooSymbolMap bypassed (direct HttpYahooChartClient).
/// Phase 2 (full expectancy pipeline) is NOT run here - only instruments that survive the corrected screen
/// warrant it, and that decision is left to the report.
/// </summary>
public sealed class CrossInstrumentSerialDependenceTests
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _log = new();
    public CrossInstrumentSerialDependenceTests(ITestOutputHelper output) => _output = output;
    private void W(string s) { _output.WriteLine(s); _log.Add(s); }

    private static readonly int[] Lags = { 1, 2, 3, 5, 10, 20 };
    private static readonly int[] VrQ = { 2, 3, 5, 10, 20 };

    // (label, Yahoo ticker, in the correction family?)   references are shown but excluded from Holm.
    private static readonly (string Label, string Ticker, bool Family)[] Battery =
    {
        ("MES  (S&P e-micro, REF)",  "MES=F",  false),
        ("ES   (S&P e-mini, REF)",   "ES=F",   false),
        ("NQ   (Nasdaq-100, tech)",  "NQ=F",   true),
        ("RTY  (Russell 2000)",      "RTY=F",  true),
        ("ZN   (10Y T-Note)",        "ZN=F",   true),
        ("ZB   (30Y T-Bond)",        "ZB=F",   true),
        ("6E   (EUR/USD)",           "6E=F",   true),
        ("CL   (WTI crude)",         "CL=F",   true),
        ("GC   (gold)",              "GC=F",   true),
        ("SI   (silver)",            "SI=F",   true),
    };

    private sealed record Res(string Label, bool Family, int N, double Rho1, double Band,
        int AcfSig, double Vr2, double Vr2p, double MinVrP, double RunsZ, double RunsP,
        double RawMinP, double WithinAdjP);

    [Fact]
    public void Screen_SerialDependence_Across_Instruments()
    {
        var client = new HttpYahooChartClient();
        DateTime to = DateTime.UtcNow;
        DateTime from = to.AddDays(-720);

        W($"QDE-020 cross-instrument serial-dependence screen  ({to:O})");
        W("H1 native, ~720 calendar days. Log returns, contiguous bars only (gap <= 1.5x interval).");
        W("Tests per instrument: ACF lags {1,2,3,5,10,20} (Bartlett 95%), VR(q) q in {2,3,5,10,20} (Lo-MacKinlay,");
        W("heteroskedasticity-robust, production VarianceRatioStatistics.Compute), Wald-Wolfowitz runs test.");
        W("Within-instrument multiplicity: Bonferroni x6 over {VR(2..20), runs}. Across the 8-instrument");
        W("family (references MES/ES excluded): Holm-Bonferroni at family-wise alpha = 0.05.");
        W("");

        var results = new List<Res>();
        foreach ((string label, string ticker, bool family) in Battery)
        {
            double[] r;
            try
            {
                string json = client.FetchChartJson(ticker, "1h", from, to, CancellationToken.None);
                YahooChartParser.ParseResult p = YahooChartParser.Parse(json);
                r = LogReturns(p.Bars, TimeSpan.FromHours(1));
            }
            catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                W($"[{label}] SKIPPED — {ex.GetType().Name}: {Trim(ex.Message)}");
                continue;
            }
            if (r.Length < 500) { W($"[{label}] SKIPPED — only {r.Length} returns"); continue; }

            int n = r.Length;
            double mean = r.Average();
            double var0 = r.Sum(x => (x - mean) * (x - mean)) / n;
            double band = 1.96 / Math.Sqrt(n);

            // ACF + Bartlett
            int maxLag = Lags.Max();
            var rho = new double[maxLag + 1];
            for (int k = 1; k <= maxLag; k++)
            {
                double acf = 0;
                for (int i = k; i < n; i++) acf += (r[i] - mean) * (r[i - k] - mean);
                rho[k] = acf / (n * var0);
            }
            int acfSig = 0;
            double running = 0;
            for (int k = 1; k <= maxLag; k++)
            {
                double seK = Math.Sqrt((1.0 + 2.0 * running) / n);
                if (Lags.Contains(k) && Math.Abs(rho[k]) > 1.96 * seK) acfSig++;
                running += rho[k] * rho[k];
            }

            // VR(q)
            var vrP = new List<double>();
            double vr2 = double.NaN, vr2p = 1.0;
            foreach (int q in VrQ)
            {
                VarianceRatioResult vr = VarianceRatioStatistics.Compute(r, q);
                if (!vr.IsValid) continue;
                vrP.Add(vr.PValue);
                if (q == 2) { vr2 = vr.VarianceRatio; vr2p = vr.PValue; }
            }
            double minVrP = vrP.Count > 0 ? vrP.Min() : 1.0;

            // runs
            RunsTest(r, out _, out _, out double runsZ, out double runsP, out _, out _);

            double rawMinP = Math.Min(minVrP, runsP);
            double withinAdj = Math.Min(1.0, 6.0 * rawMinP);

            results.Add(new Res(label, family, n, rho[1], band, acfSig, vr2, vr2p, minVrP, runsZ, runsP, rawMinP, withinAdj));
        }

        // ── Holm across the family ────────────────────────────────────────────────────────────────
        var fam = results.Where(x => x.Family).OrderBy(x => x.WithinAdjP).ToList();
        int m = fam.Count;
        var holmSig = new Dictionary<string, (double crit, bool sig)>();
        bool stillRejecting = true;
        for (int i = 0; i < m; i++)
        {
            double crit = 0.05 / (m - i);
            bool sig = stillRejecting && fam[i].WithinAdjP <= crit;
            if (!sig) stillRejecting = false;
            holmSig[fam[i].Label] = (crit, sig);
        }

        W("=== PHASE 1 — screening table ===");
        W(string.Format("{0,-26} {1,7} {2,10} {3,6} {4,9} {5,9} {6,9} {7,10} {8,11} {9}",
            "instrument", "N", "rho(1)", "ACFsig", "VR(2)", "VR(2)p", "minVR p", "runs p", "within-adj p", "Holm verdict"));
        W(new string('-', 130));
        foreach (Res x in results)
        {
            string verdict;
            if (!x.Family) verdict = "(reference, not corrected)";
            else if (holmSig.TryGetValue(x.Label, out var h))
                verdict = h.sig ? $"*** SIGNIFICANT (<= {h.crit:0.0000})" : $"not sig (crit {h.crit:0.0000})";
            else verdict = "n/a";
            W(string.Format(CultureInfo.InvariantCulture,
                "{0,-26} {1,7} {2,10:+0.0000;-0.0000} {3,6} {4,9:0.0000} {5,9:0.0000} {6,9:0.0000} {7,10:0.0000} {8,11:0.0000}  {9}",
                x.Label, x.N, x.Rho1, x.AcfSig, x.Vr2, x.Vr2p, x.MinVrP, x.RunsP, x.WithinAdjP, verdict));
        }

        W("");
        W("Legend: ACFsig = # of lags in {1,2,3,5,10,20} exceeding the Bartlett 95% band (of 6).");
        W("        rho(1) band = +/-" + (results.Count > 0 ? results[0].Band : 0).ToString("0.0000", CultureInfo.InvariantCulture) +
          " (approx; per-instrument N varies).");
        W("        within-adj p = min(1, 6 x min(VR(2..20) p, runs p)).  Holm applied to that column over the 8-instrument family.");

        // ── temporal robustness (60/40) for every RAW-significant family instrument ───────────────
        W("");
        W("=== temporal robustness (60/40 split) for raw-significant family instruments (rawMinP < 0.05) ===");
        foreach ((string label, string ticker, bool family) in Battery)
        {
            if (!family) continue;
            Res? rr = results.FirstOrDefault(x => x.Label == label);
            if (rr is null || rr.RawMinP >= 0.05) continue;
            double[] r;
            try
            {
                string json = client.FetchChartJson(ticker, "1h", from, to, CancellationToken.None);
                r = LogReturns(YahooChartParser.Parse(json).Bars, TimeSpan.FromHours(1));
            }
            catch { continue; }
            int cut = (int)(r.Length * 0.60);
            double[] tr = r.Take(cut).ToArray(), oo = r.Skip(cut).ToArray();
            foreach ((string half, double[] hr) in new[] { ("TRAIN", tr), ("OOS  ", oo) })
            {
                VarianceRatioResult v2 = VarianceRatioStatistics.Compute(hr, 2);
                RunsTest(hr, out _, out _, out double rz, out double rp, out _, out _);
                double mean = hr.Average(), var0 = hr.Sum(x => (x - mean) * (x - mean)) / hr.Length;
                double acf1 = 0; for (int i = 1; i < hr.Length; i++) acf1 += (hr[i] - mean) * (hr[i - 1] - mean);
                acf1 /= hr.Length * var0;
                W($"  {label,-24} {half}  n={hr.Length,6}  rho(1)={acf1,8:+0.0000;-0.0000} (band +/-{1.96 / Math.Sqrt(hr.Length):0.0000})  " +
                  $"VR(2)={v2.VarianceRatio:0.0000} p={v2.PValue:0.0000}  runs Z={rz:+0.00} p={rp:0.0000}");
            }
        }

        var survivors = results.Where(x => x.Family && holmSig.TryGetValue(x.Label, out var h) && h.sig).ToList();
        W("");
        if (survivors.Count == 0)
        {
            W("=== VERDICT ===");
            W("NO instrument survives the Holm-corrected screen at family-wise alpha = 0.05.");
            W("Serial-independence at H1 is a GENERAL PROPERTY of retail futures at this timescale,");
            W("not a peculiarity of MES. Phase 2 (full expectancy pipeline) is NOT warranted for any instrument.");
        }
        else
        {
            W("=== SURVIVORS (warrant Phase 2) ===");
            foreach (Res s in survivors)
            {
                W($"  {s.Label}: within-adj p={s.WithinAdjP:0.0000}, VR(2)={s.Vr2:0.0000} (p={s.Vr2p:0.0000}), " +
                  $"rho(1)={s.Rho1:+0.0000} (band +/-{s.Band:0.0000}), runs Z={s.RunsZ:+0.00}");
                W($"     economic pre-check: |rho(1)| = {Math.Abs(s.Rho1):0.0000} -> predictable fraction of next-bar move; " +
                  $"a linear-autocorr strategy captures ~|rho(1)| x sigma per bet, cost is one round-trip.");
                W($"     -> temporal robustness + per-instrument realistic cost must be established before concluding an edge.");
            }
        }

        WriteReport();
        Assert.SkipUnless(results.Count >= 4, "Yahoo unavailable for most instruments this run.");
        Assert.True(results.Count >= 4);
    }

    // ── stats helpers (verbatim from QDE-012 §2.h MesSerialDependenceAuditTests) ─────────────────
    private static double[] LogReturns(IReadOnlyList<HistoricalBar> bars, TimeSpan interval)
    {
        var outp = new List<double>(bars.Count);
        long maxGap = (long)(interval.Ticks * 1.5);
        for (int i = 1; i < bars.Count; i++)
        {
            long gap = bars[i].Timestamp.Ticks - bars[i - 1].Timestamp.Ticks;
            if (gap <= 0 || gap > maxGap) continue;
            double c0 = (double)bars[i - 1].Close, c1 = (double)bars[i].Close;
            if (c0 <= 0 || c1 <= 0) continue;
            double lr = Math.Log(c1 / c0);
            if (double.IsFinite(lr)) outp.Add(lr);
        }
        return outp.ToArray();
    }

    private static void RunsTest(double[] r, out int runs, out double eRuns, out double z, out double p, out int nPos, out int nNeg)
    {
        nPos = r.Count(x => x > 0);
        nNeg = r.Count(x => x < 0);
        int n = nPos + nNeg;
        runs = 1;
        int prevSign = 0;
        foreach (double x in r)
        {
            int s = x > 0 ? 1 : x < 0 ? -1 : 0;
            if (s == 0) continue;
            if (prevSign != 0 && s != prevSign) runs++;
            prevSign = s;
        }
        double np = nPos, nn = nNeg, nn2 = n;
        eRuns = 2.0 * np * nn / nn2 + 1.0;
        double varRuns = 2.0 * np * nn * (2.0 * np * nn - nn2) / (nn2 * nn2 * (nn2 - 1.0));
        z = varRuns > 0 ? (runs - eRuns) / Math.Sqrt(varRuns) : double.NaN;
        p = double.IsFinite(z) ? 2.0 * (1.0 - NormCdf(Math.Abs(z))) : double.NaN;
    }

    private static double NormCdf(double x)
    {
        double t = 1.0 / (1.0 + 0.2316419 * Math.Abs(x));
        double poly = t * (0.319381530 + t * (-0.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
        double d = Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI);
        double cdf = 1.0 - d * poly;
        return x < 0 ? 1.0 - cdf : cdf;
    }

    private static string Trim(string s) => s.Length <= 100 ? s : s[..100] + "...";

    private void WriteReport()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) return;
        string od = Path.Combine(dir, "Research", "OrderFlowFeasibility", "Output");
        Directory.CreateDirectory(od);
        File.WriteAllText(Path.Combine(od, "cross_instrument_serial_dependence.txt"), string.Join("\n", _log) + "\n");
    }
}
