using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;
using IQIAIndicator.Tests.BacktestTests.Yahoo;
using Xunit;

namespace IQIAIndicator.Tests.Research.StopLossContractAudit;

/// <summary>
/// READ-ONLY. 2026-08-31 audit "existence de structure exploitable sur MES (autocorrelation brute)".
/// NOT a strategy test - a pure statistical characterisation of MES returns, independent of any
/// IQIAIndicator model. ACF (Bartlett CI), Lo-MacKinlay variance ratio (reuses the production
/// VarianceRatioStatistics.Compute unchanged), runs test on return sign, economic-significance scaling,
/// and a train/OOS split. Same series as the prior audits: M5 59d, M15 (agg), H1 native 720d.
/// </summary>
public sealed class MesSerialDependenceAuditTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public MesSerialDependenceAuditTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private const decimal PointValue = 5m;                       // MES $/point
    private const double BaseCostRoundTripUsd = 3.855;           // same as prior audits
    private static readonly int[] Lags = { 1, 2, 3, 5, 10, 20 };

    private readonly List<string> _log = new();
    private void W(string s) { _output.WriteLine(s); _log.Add(s); }

    [Fact]
    public void Characterise_MES_Serial_Dependence_M5_M15_H1()
    {
        HistoricalSeries m5;
        try { m5 = _yahoo.Require(); }
        catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var sets = new List<(string Tf, TimeSpan Interval, IReadOnlyList<HistoricalBar> Bars)>
        {
            ("M5  (59d)", TimeSpan.FromMinutes(5), m5.Bars),
            ("M15 (agg 59d)", TimeSpan.FromMinutes(15), AggBars(m5.Bars, TimeSpan.FromMinutes(15))),
        };
        try
        {
            var client = new HttpYahooChartClient();
            string json = client.FetchChartJson(YahooSymbolMap.Resolve("MES"), "1h",
                DateTime.UtcNow.AddDays(-720), DateTime.UtcNow, CancellationToken.None);
            YahooChartParser.ParseResult p = YahooChartParser.Parse(json);
            sets.Add(("H1  native (720d)", TimeSpan.FromHours(1), p.Bars));
            W($"native 1h: {p.Bars.Count} bars ({p.GapCount} gaps omitted by parser)");
        }
        catch (Exception ex) { W($"native 1h pull failed ({ex.GetType().Name}: {ex.Message})"); }

        W("Returns = natural log returns ln(C_t / C_{t-1}), computed only between CONSECUTIVE bars whose");
        W("timestamp spacing <= 1.5x the nominal interval (drops overnight / weekend / CME-break jumps).");
        W("ACF CI = Bartlett 95% (SE_k = sqrt((1 + 2*sum_{j<k} rho_j^2)/N)); white-noise band = +/-1.96/sqrt(N).");

        foreach ((string tf, TimeSpan interval, IReadOnlyList<HistoricalBar> bars) in sets)
        {
            W("");
            W("########################################################################");
            W($"###  {tf}");
            W("########################################################################");
            double[] r = LogReturns(bars, interval, out int rawN, out int keptN);
            W($"  bars={bars.Count}  raw returns={rawN}  kept (contiguous)={keptN}  " +
              $"dropped={rawN - keptN} ({100.0 * (rawN - keptN) / Math.Max(1, rawN):0.0}%)");
            if (r.Length < 200) { W("  too few returns - skipped."); continue; }

            Analyse("FULL", r, priceSigmaPt: PriceSigmaPoints(bars, interval));

            // train / OOS
            int cut = (int)(r.Length * 0.60);
            Analyse("TRAIN(60%)", r.Take(cut).ToArray(), priceSigmaPt: double.NaN, brief: true);
            Analyse("OOS(40%)", r.Skip(cut).ToArray(), priceSigmaPt: double.NaN, brief: true);
        }

        WriteReport();
    }

    private void Analyse(string label, double[] r, double priceSigmaPt, bool brief = false)
    {
        int n = r.Length;
        double mean = r.Average();
        double var0 = r.Sum(x => (x - mean) * (x - mean)) / n;
        double sd = Math.Sqrt(var0);
        double wnBand = 1.96 / Math.Sqrt(n);

        W("");
        W($"  --- {label}  n={n}  mean={mean:E3}  sd={sd:E4}  (white-noise ACF band +/-{wnBand:0.0000}) ---");

        // ACF with Bartlett CI
        var rho = new Dictionary<int, double>();
        W("  lag |   rho    | Bartlett95 +/- | |rho|>band? | sign");
        int maxLag = Lags.Max();
        var allRho = new double[maxLag + 1];
        for (int k = 1; k <= maxLag; k++)
        {
            double acf = 0.0;
            for (int i = k; i < n; i++) acf += (r[i] - mean) * (r[i - k] - mean);
            acf /= (n * var0);
            allRho[k] = acf;
        }
        // proper Bartlett: SE_k uses sum of rho_j^2 for j=1..k-1
        double running = 0.0;
        for (int k = 1; k <= maxLag; k++)
        {
            double seK = Math.Sqrt((1.0 + 2.0 * running) / n);
            if (Lags.Contains(k))
            {
                double ci = 1.96 * seK;
                bool sig = Math.Abs(allRho[k]) > ci;
                rho[k] = allRho[k];
                W($"  {k,3} | {allRho[k],+8:0.0000} | +/-{ci,10:0.0000} | {(sig ? "  YES  " : "  no   ")} | {(allRho[k] > 0 ? "momentum(+)" : "reversion(-)")}");
            }
            running += allRho[k] * allRho[k];
        }

        // Lo-MacKinlay variance ratio (production statistics, unchanged)
        W("  VR(q) Lo-MacKinlay (heteroskedasticity-robust):  VR<1 => reversion, VR>1 => momentum");
        W("   q  |  VR      |  Z       |  p-value  | signif(5%)");
        foreach (int q in new[] { 2, 3, 5, 10, 20 })
        {
            VarianceRatioResult vr = VarianceRatioStatistics.Compute(r, q);
            if (!vr.IsValid) { W($"  {q,3} | invalid: {vr.Explanation}"); continue; }
            W($"  {q,3} | {vr.VarianceRatio,7:0.0000} | {vr.ZStatistic,+7:0.000} | {vr.PValue,9:0.0000} | {(vr.PValue < 0.05 ? "  YES" : "  no")}");
        }

        // Runs test on sign of returns
        RunsTest(r, out int runs, out double eRuns, out double zRuns, out double pRuns, out int nPos, out int nNeg);
        W($"  Runs test (sign): runs={runs}  E[runs]={eRuns:0.0}  Z={zRuns:+0.000}  p={pRuns:0.0000}  " +
          $"({(pRuns < 0.05 ? "SIGNIFICANT" : "not significant")}; n+={nPos} n-={nNeg})  " +
          $"{(zRuns < 0 ? "fewer runs => positive dependence / clustering" : "more runs => negative dependence / alternation")}");

        if (brief) return;

        // Economic significance of lag-1
        W("");
        W("  --- economic significance ---");
        double rho1 = rho.TryGetValue(1, out double v) ? v : 0.0;
        double predBarReturnFrac = rho1 * sd;                        // predictable component of next 1-bar log return
        double predBarPts = double.IsFinite(priceSigmaPt) ? Math.Abs(rho1) * priceSigmaPt : double.NaN;
        W($"  lag-1 rho = {rho1:+0.0000}");
        W($"  predictable component of next-bar return |rho1|*sd = {Math.Abs(predBarReturnFrac):E3} (fraction) " +
          (double.IsFinite(predBarPts) ? $"= {predBarPts:0.000} points" : "(price-sigma n/a)"));
        W($"  MES round-trip cost (base) = {BaseCostRoundTripUsd / (double)PointValue:0.00} points per trade");
        if (double.IsFinite(predBarPts))
        {
            double ratio = predBarPts / (BaseCostRoundTripUsd / (double)PointValue);
            W($"  predictable move / round-trip cost = {ratio:0.000}   " +
              $"(needs > 1.0 for a bar-by-bar linear-autocorr strategy to clear ONE round trip)");
            W($"  edge in R (predictable move / 2-sigma stop) ~ |rho1|/2 = {Math.Abs(rho1) / 2.0:0.0000} R/trade  " +
              $"vs cost ~{(BaseCostRoundTripUsd / (double)PointValue) / (priceSigmaPt * 2.0):0.0000} R/trade");
        }
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

    private static double[] LogReturns(IReadOnlyList<HistoricalBar> bars, TimeSpan interval, out int rawN, out int keptN)
    {
        var outp = new List<double>(bars.Count);
        rawN = 0;
        long maxGap = (long)(interval.Ticks * 1.5);
        for (int i = 1; i < bars.Count; i++)
        {
            rawN++;
            long gap = bars[i].Timestamp.Ticks - bars[i - 1].Timestamp.Ticks;
            if (gap <= 0 || gap > maxGap) continue;
            double c0 = (double)bars[i - 1].Close, c1 = (double)bars[i].Close;
            if (c0 <= 0 || c1 <= 0) continue;
            double lr = Math.Log(c1 / c0);
            if (double.IsFinite(lr)) outp.Add(lr);
        }
        keptN = outp.Count;
        return outp.ToArray();
    }

    /// <summary>Median absolute 1-bar close-to-close move in POINTS, contiguous bars only - the price-scale
    /// sigma proxy used to convert a dimensionless autocorrelation into points.</summary>
    private static double PriceSigmaPoints(IReadOnlyList<HistoricalBar> bars, TimeSpan interval)
    {
        var moves = new List<double>();
        long maxGap = (long)(interval.Ticks * 1.5);
        for (int i = 1; i < bars.Count; i++)
        {
            long gap = bars[i].Timestamp.Ticks - bars[i - 1].Timestamp.Ticks;
            if (gap <= 0 || gap > maxGap) continue;
            moves.Add(Math.Abs((double)(bars[i].Close - bars[i - 1].Close)));
        }
        if (moves.Count == 0) return double.NaN;
        moves.Sort();
        // use standard deviation of signed moves for a true sigma
        var signed = new List<double>();
        for (int i = 1; i < bars.Count; i++)
        {
            long gap = bars[i].Timestamp.Ticks - bars[i - 1].Timestamp.Ticks;
            if (gap <= 0 || gap > maxGap) continue;
            signed.Add((double)(bars[i].Close - bars[i - 1].Close));
        }
        double m = signed.Average();
        return Math.Sqrt(signed.Sum(x => (x - m) * (x - m)) / signed.Count);
    }

    private static IReadOnlyList<HistoricalBar> AggBars(IReadOnlyList<HistoricalBar> m5, TimeSpan bucket)
    {
        long t = bucket.Ticks;
        var outp = new List<HistoricalBar>();
        DateTime key = default; decimal o = 0, h = 0, l = 0, c = 0, v = 0; bool open = false;
        foreach (HistoricalBar b in m5)
        {
            DateTime k = new(b.Timestamp.Ticks - b.Timestamp.Ticks % t, DateTimeKind.Utc);
            if (!open) { key = k; o = b.Open; h = b.High; l = b.Low; c = b.Close; v = b.Volume; open = true; }
            else if (k == key) { h = Math.Max(h, b.High); l = Math.Min(l, b.Low); c = b.Close; v += b.Volume; }
            else { outp.Add(new HistoricalBar(key, o, h, l, c, v)); key = k; o = b.Open; h = b.High; l = b.Low; c = b.Close; v = b.Volume; }
        }
        if (open) outp.Add(new HistoricalBar(key, o, h, l, c, v));
        return outp;
    }

    private void WriteReport()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) return;
        string od = Path.Combine(dir, "Research", "StopLossContractAudit", "Output");
        Directory.CreateDirectory(od);
        File.WriteAllText(Path.Combine(od, "serial_dependence.txt"), string.Join("\n", _log) + "\n");
    }
}
