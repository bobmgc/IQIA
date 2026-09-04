using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Tests.Research.OrderFlowFeasibility;
using Xunit;

namespace IQIAIndicator.Tests.Research.CalendarPairTrading;

/// <summary>
/// QDE-021 — READ-ONLY. Two intraday-only research axes, post-QDE-020:
///   Axis A — calendar / hour-of-session effects on MES returns (H1 native ~720d + M5 native from the
///            QDE-017 order-flow capture ~349d). Partition returns by day-of-week, session block and
///            position-in-month; test each partition mean vs the unconditioned baseline with a Welch
///            two-sample test and a Holm multiple-comparison correction across the whole axis.
///   Axis B — MES vs NQ pair-trading as a self-contained mean-reversion spread strategy (H1 native
///            ~720d). Rolling z-score of the log-price ratio; enter on |z| >= theta (theta calibrated
///            on TRAIN only), close the position MANDATORILY before the RTH session close — never
///            overnight (prop-firm constraint). Net of BOTH legs' realistic round-trip cost.
///
/// No production type modified. No entry added to YahooSymbolMap (the probe calls
/// HttpYahooChartClient.FetchChartJson directly, as in QDE-013/014/015/020). No threshold calibrated on
/// the full sample. Every simulated position is force-flat at the session close — no bar is held
/// overnight in either axis.
/// </summary>
public sealed class CalendarAndPairTradingTests
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _log = new();
    public CalendarAndPairTradingTests(ITestOutputHelper output) => _output = output;
    private void W(string s) { _output.WriteLine(s); _log.Add(s); }

    private const double AlphaFamily = 0.05;
    private const double TrainFraction = 0.60;
    private const int PurgeBarsH1 = 20;

    // MES micro round-trip cost (identical to QDE-012+/QDE-014/QDE-017: 0.77 pt == ~$3.855 at $5/pt).
    private const double MesRoundTripPts = 0.77;
    private const double MesRoundTripUsd = 3.855;
    private const double MesPointValue = 5.0;

    // NQ leg cost — sourced from CME contract specs (as done for ZN in QDE-020), NOT reused from MES.
    //   MNQ  (Micro E-mini Nasdaq-100): $2 / index point, tick 0.25 = $0.50. Retail round-trip
    //        ~= commission $1.04 RT + 1 tick spread crossing $0.50  ==>  ~$1.54 all-in.
    //   NQ   (E-mini Nasdaq-100):       $20 / index point, tick 0.25 = $5.00. Retail round-trip
    //        ~= commission $2.50 RT + 1 tick $5.00  ==>  ~$7.50 all-in.
    private const double MnqPointValue = 2.0;
    private const double MnqRoundTripUsd = 1.54;
    private const double NqPointValue = 20.0;
    private const double NqRoundTripUsd = 7.50;

    private static readonly TimeZoneInfo Eastern = ResolveEastern();
    private static TimeZoneInfo ResolveEastern()
    {
        foreach (string id in new[] { "Eastern Standard Time", "America/New_York" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { /* try next */ }
        return TimeZoneInfo.Utc;
    }
    private static DateTime ToEt(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Eastern);

    // Minutes-from-ET-midnight helpers for session-block classification.
    private static int EtMinutes(DateTime et) => et.Hour * 60 + et.Minute;
    private const int RthOpen = 9 * 60 + 30;   // 09:30 ET
    private const int RthClose = 16 * 60;       // 16:00 ET
    private static bool InRth(DateTime et)
    {
        if (et.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        int m = EtMinutes(et);
        return m >= RthOpen && m < RthClose;
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════════
    //  AXIS A — CALENDAR / HOUR-OF-SESSION EFFECTS
    // ════════════════════════════════════════════════════════════════════════════════════════════════

    private sealed record Obs(DateTime EtStart, double LogRet, double PtRet, double Price, bool Rth);

    [Fact]
    public void AxisA_Calendar_Hourly_Effects_MES()
    {
        W("################  QDE-021 AXIS A — CALENDAR / HOURLY EFFECTS (MES, intraday only)  ################");
        W($"run {DateTime.UtcNow:O}");
        W("Return attributed to the ET wall-clock START of the interval it accrues over. Day-of-week and");
        W("position-in-month partitions are measured on RTH-only returns (09:30-16:00 ET) so any survivor");
        W("is an intraday-tradeable effect; session-block partitions use ALL bars (overnight blocks are");
        W("themselves the hypothesis). Baseline = mean of the SAME universe (RTH-only vs all). Welch two-");
        W("sample test bucket-vs-complement; Holm-Bonferroni across every partition in this axis.");
        W("");

        var perRes = new List<(string Res, List<PartRes> Parts)>();

        // ---- H1 native ~720d --------------------------------------------------------------------------
        List<Obs>? h1 = null;
        try
        {
            var client = new HttpYahooChartClient();
            DateTime to = DateTime.UtcNow, from = to.AddDays(-720);
            string json = client.FetchChartJson("MES=F", "1h", from, to, CancellationToken.None);
            IReadOnlyList<HistoricalBar> bars = YahooChartParser.Parse(json).Bars;
            h1 = BuildObs(bars, TimeSpan.FromHours(1));
            W($"[H1] MES=F 1h: {bars.Count} bars -> {h1.Count} contiguous returns  " +
              $"{h1[0].EtStart:yyyy-MM-dd}..{h1[^1].EtStart:yyyy-MM-dd} ET   (RTH-only: {h1.Count(o => o.Rth)})");
        }
        catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            W($"[H1] SKIPPED — {ex.GetType().Name}: {ex.Message}");
        }

        // ---- M5 native from the QDE-017 order-flow capture (~349d) -----------------------------------
        List<Obs>? m5 = null;
        string? csv = OrderFlowCsvBarSource.FindLatest();
        if (csv is not null && File.Exists(csv))
        {
            IReadOnlyList<OrderFlowBar> rows = OrderFlowCsvBarSource.ReadClean(csv);
            var mb = rows.Select(r => new HistoricalBar(r.Timestamp, r.Open, r.High, r.Low, r.Close, r.Volume)).ToList();
            m5 = BuildObs(mb, TimeSpan.FromMinutes(5));
            W($"[M5] {Path.GetFileName(csv)}: {rows.Count} clean bars -> {m5.Count} contiguous returns  " +
              $"{m5[0].EtStart:yyyy-MM-dd}..{m5[^1].EtStart:yyyy-MM-dd} ET   (RTH-only: {m5.Count(o => o.Rth)})");
        }
        else
        {
            W("[M5] SKIPPED — no orderflow_export_*.csv under %LOCALAPPDATA%\\IQIA\\orderflow.");
        }

        Assert.SkipUnless(h1 is not null || m5 is not null, "Neither the H1 (Yahoo) nor the M5 (capture) source was available this run.");

        if (h1 is not null) perRes.Add(("H1", RunAxisA(h1, "H1", MesRoundTripPts)));
        if (m5 is not null) perRes.Add(("M5", RunAxisA(m5, "M5", MesRoundTripPts)));

        // ---- axis-wide Holm across every partition of every resolution ------------------------------
        W("");
        W("================  AXIS-WIDE HOLM (all partitions, all resolutions)  ================");
        var all = perRes.SelectMany(pr => pr.Parts.Select(p => (Key: $"{pr.Res}:{p.Label}", p.P, p))).ToList();
        var holm = Holm(all.Select(x => x.P).ToArray(), AlphaFamily);
        var ordered = all.Select((x, i) => (x.Key, x.P, x.p, Sig: holm[i])).OrderBy(x => x.P).ToList();
        W($"family size m = {all.Count}   family-wise alpha = {AlphaFamily}");
        W(string.Format("{0,-26} {1,10} {2,12} {3,12} {4,10} {5}", "partition", "n", "meanRet", "baseRet", "p", "Holm"));
        W(new string('-', 92));
        foreach (var x in ordered)
            W(string.Format(CultureInfo.InvariantCulture, "{0,-26} {1,10} {2,12:+0.00e-000;-0.00e-000} {3,12:+0.00e-000;-0.00e-000} {4,10:0.0000} {5}",
                x.Key, x.p.N, x.p.Mean, x.p.Base, x.P, x.Sig ? "*** SIGNIFICANT" : "not sig"));

        var survivors = ordered.Where(x => x.Sig).ToList();
        W("");
        if (survivors.Count == 0)
        {
            W("=== AXIS A VERDICT: no partition survives the axis-wide Holm correction. No calendar/hourly");
            W("    effect on MES intraday returns is established. Net-of-cost / purged-OOS step not reached.");
        }
        else
        {
            W("=== AXIS A — survivors advance to net-of-cost + purged train/OOS ===");
            foreach (var s in survivors)
            {
                PartRes p = s.p;
                double meanPts = p.MeanPts, ptCost = p.RoundTripPts;
                W($"  {s.Key}: mean/bar = {p.Mean:+0.00e-000} ({meanPts:+0.0000} pt)   round-trip cost = {ptCost:0.0000} pt");
                W($"      net/bar = {meanPts - ptCost:+0.0000} pt   " +
                  $"({(Math.Abs(meanPts) > ptCost ? "exceeds cost — model as a trade below" : "BELOW cost — not tradeable")})");
                W($"      purged split (RTH-time order): TRAIN mean {p.TrainMean:+0.00e-000} (n={p.TrainN})   " +
                  $"OOS mean {p.OosMean:+0.00e-000} (n={p.OosN})   " +
                  $"{(Math.Sign(p.TrainMean) == Math.Sign(p.OosMean) && p.OosN > 30 ? "sign STABLE" : "sign UNSTABLE / thin OOS")}");
            }
        }

        WriteReport("qde021_axisA_calendar.txt");
        Assert.True(perRes.Count >= 1);
    }

    private sealed record PartRes(string Label, int N, double Mean, double Base, double Diff, double T, double P,
        double MeanPts, double RoundTripPts, double TrainMean, int TrainN, double OosMean, int OosN);

    private List<PartRes> RunAxisA(List<Obs> obs, string res, double roundTripPts)
    {
        W("");
        W($"================  RESOLUTION {res}  (n={obs.Count}, RTH-only n={obs.Count(o => o.Rth)})  ================");

        var rth = obs.Where(o => o.Rth).ToList();
        double baseAll = obs.Average(o => o.LogRet);
        double baseRth = rth.Count > 0 ? rth.Average(o => o.LogRet) : 0.0;
        double meanPrice = obs.Average(o => o.Price);
        W($"baseline mean/bar: ALL = {baseAll:+0.00e-000;-0.00e-000}   RTH-only = {baseRth:+0.00e-000;-0.00e-000}   mean price = {meanPrice:0.0}");

        var parts = new List<PartRes>();

        // (1) DAY OF WEEK  — RTH-only universe
        foreach (DayOfWeek d in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
            parts.Add(Partition(rth, $"DOW {d.ToString()[..3]}", o => o.EtStart.DayOfWeek == d, meanPrice, roundTripPts));

        // (2) SESSION BLOCK — ALL-bars universe
        (string Label, Func<Obs, bool> Sel)[] blocks =
        {
            ("blk RTHopen 0930-1030", o => o.EtStart.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && Between(o, RthOpen, 10 * 60 + 30)),
            ("blk RTHmid 1100-1400",  o => o.EtStart.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && Between(o, 11 * 60, 14 * 60)),
            ("blk RTHlast 1500-1600", o => o.EtStart.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && Between(o, 15 * 60, 16 * 60)),
            ("blk ON 1800-2200",      o => Between(o, 18 * 60, 22 * 60)),
            ("blk ON 2200-0200",      o => EtMinutes(o.EtStart) >= 22 * 60 || EtMinutes(o.EtStart) < 2 * 60),
            ("blk ON 0200-0600",      o => Between(o, 2 * 60, 6 * 60)),
            ("blk ON 0600-0930",      o => Between(o, 6 * 60, RthOpen)),
        };
        foreach ((string lbl, Func<Obs, bool> sel) in blocks)
            parts.Add(Partition(obs, lbl, sel, meanPrice, roundTripPts));

        // (3) POSITION IN MONTH — RTH-only universe. Trading-day ordinal within (ET year, ET month).
        var etDates = rth.Select(o => o.EtStart.Date).Distinct().OrderBy(x => x).ToList();
        var ordFromStart = new Dictionary<DateTime, int>();
        var ordFromEnd = new Dictionary<DateTime, int>();
        foreach (var g in etDates.GroupBy(x => (x.Year, x.Month)))
        {
            var days = g.OrderBy(x => x).ToList();
            for (int i = 0; i < days.Count; i++) { ordFromStart[days[i]] = i + 1; ordFromEnd[days[i]] = days.Count - i; }
        }
        parts.Add(Partition(rth, "PIM first-3-TD", o => ordFromStart.TryGetValue(o.EtStart.Date, out int k) && k <= 3, meanPrice, roundTripPts));
        parts.Add(Partition(rth, "PIM last-3-TD", o => ordFromEnd.TryGetValue(o.EtStart.Date, out int k) && k <= 3, meanPrice, roundTripPts));
        parts.Add(Partition(rth, "PIM middle", o => ordFromStart.TryGetValue(o.EtStart.Date, out int k) && k > 3
                                                    && ordFromEnd.TryGetValue(o.EtStart.Date, out int j) && j > 3, meanPrice, roundTripPts));

        // per-resolution Holm (reported; the binding correction is the axis-wide one in the caller)
        var pr = Holm(parts.Select(p => p.P).ToArray(), AlphaFamily);
        W("");
        W(string.Format("{0,-24} {1,8} {2,13} {3,13} {4,10} {5,8} {6}", "partition", "n", "meanRet", "diff-vs-base", "t", "p", "Holm(res)"));
        W(new string('-', 96));
        var poH = parts.Select((p, i) => (p, sig: pr[i])).OrderBy(x => x.p.P).ToList();
        foreach (var (p, sig) in poH)
            W(string.Format(CultureInfo.InvariantCulture, "{0,-24} {1,8} {2,13:+0.00e-000;-0.00e-000} {3,13:+0.00e-000;-0.00e-000} {4,8:+0.00;-0.00} {5,10:0.0000} {6}",
                p.Label, p.N, p.Mean, p.Diff, p.T, p.P, sig ? "*** sig" : "-"));

        return parts;
    }

    private static bool Between(Obs o, int fromMin, int toMin) { int m = EtMinutes(o.EtStart); return m >= fromMin && m < toMin; }

    private PartRes Partition(List<Obs> universe, string label, Func<Obs, bool> sel, double meanPrice, double roundTripPts)
    {
        var inb = new List<double>();
        var outb = new List<double>();
        var inbTimed = new List<(DateTime t, double r)>();
        foreach (Obs o in universe)
        {
            if (sel(o)) { inb.Add(o.LogRet); inbTimed.Add((o.EtStart, o.LogRet)); }
            else outb.Add(o.LogRet);
        }
        double baseMean = universe.Count > 0 ? universe.Average(o => o.LogRet) : 0.0;
        if (inb.Count < 20)
            return new PartRes(label, inb.Count, inb.Count > 0 ? inb.Average() : 0.0, baseMean, 0, 0, 1.0, 0, roundTripPts, 0, 0, 0, 0);

        (double m1, double v1, int n1) = MVN(inb);
        (double m2, double v2, int n2) = MVN(outb);
        double se = Math.Sqrt(v1 / n1 + v2 / n2);
        double t = se > 0 ? (m1 - m2) / se : 0.0;
        double p = 2.0 * (1.0 - NormCdf(Math.Abs(t)));

        // purged split in time order of the in-bucket observations
        var seq = inbTimed.OrderBy(x => x.t).ToList();
        int cut = (int)(seq.Count * TrainFraction);
        int purge = Math.Max(1, seq.Count / 100);
        double trM = seq.Take(Math.Max(0, cut - purge)).Select(x => x.r).DefaultIfEmpty(0).Average();
        int trN = Math.Max(0, cut - purge);
        var oo = seq.Skip(cut).Select(x => x.r).ToList();
        double ooM = oo.Count > 0 ? oo.Average() : 0.0;

        return new PartRes(label, inb.Count, m1, baseMean, m1 - baseMean, t, p,
            m1 * meanPrice, roundTripPts, trM, trN, ooM, oo.Count);
    }

    private static List<Obs> BuildObs(IReadOnlyList<HistoricalBar> bars, TimeSpan interval)
    {
        var o = new List<Obs>(bars.Count);
        long maxGap = (long)(interval.Ticks * 1.5);
        for (int i = 1; i < bars.Count; i++)
        {
            long gap = bars[i].Timestamp.Ticks - bars[i - 1].Timestamp.Ticks;
            if (gap <= 0 || gap > maxGap) continue;
            double c0 = (double)bars[i - 1].Close, c1 = (double)bars[i].Close;
            if (c0 <= 0 || c1 <= 0) continue;
            double lr = Math.Log(c1 / c0);
            if (!double.IsFinite(lr)) continue;
            DateTime etStart = ToEt(bars[i - 1].Timestamp);
            o.Add(new Obs(etStart, lr, c1 - c0, c0, InRth(etStart)));
        }
        return o;
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════════
    //  AXIS B — MES vs NQ PAIR-TRADING (intraday only, forced flat at RTH close)
    // ════════════════════════════════════════════════════════════════════════════════════════════════

    private sealed record Pair(DateTime Utc, DateTime Et, double PMes, double PNq);
    private sealed record Trade(int EntryBar, double GrossR, double NetRmnq, double NetRnq, double EntrySd, bool InTrain);

    [Fact]
    public void AxisB_PairTrading_MES_NQ_Intraday()
    {
        W("################  QDE-021 AXIS B — MES<->NQ PAIR-TRADING (spread mean-reversion, intraday only)  ################");
        W($"run {DateTime.UtcNow:O}");

        List<Pair> px;
        try
        {
            var client = new HttpYahooChartClient();
            DateTime to = DateTime.UtcNow, from = to.AddDays(-720);
            var mes = YahooChartParser.Parse(client.FetchChartJson("MES=F", "1h", from, to, CancellationToken.None)).Bars;
            var nq = YahooChartParser.Parse(client.FetchChartJson("NQ=F", "1h", from, to, CancellationToken.None)).Bars;
            px = JoinPairs(mes, nq);
            W($"MES=F {mes.Count} bars, NQ=F {nq.Count} bars -> {px.Count} time-aligned pairs  " +
              $"{px[0].Utc:yyyy-MM-dd}..{px[^1].Utc:yyyy-MM-dd}");
        }
        catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Assert.Skip($"Yahoo unavailable: {ex.GetType().Name}: {ex.Message}");
            return;
        }
        Assert.SkipUnless(px.Count > 2000, $"only {px.Count} aligned pairs — insufficient for a purged split.");

        // spread = ln(P_MES) - ln(P_NQ). Rolling causal z: stats over [t-W, t-1], value at t.
        double[] spread = px.Select(p => Math.Log(p.PMes) - Math.Log(p.PNq)).ToArray();
        int n = spread.Length;

        int splitBar = (int)(n * TrainFraction);
        W($"purged split: TRAIN bar < {splitBar - PurgeBarsH1}, OOS bar >= {splitBar}  (n={n}, purge {PurgeBarsH1})");
        W("Entry only inside RTH [09:30,15:00) ET. Position force-flat at first bar >= 16:00 ET or on ET-date change");
        W("(never overnight). Exit also on mean touch (|z| <= 0.25) or stop (|z| >= theta + 1.5).");
        W("R unit = spread sigma at entry. Cost = BOTH legs round-trip, in spread (log) units, from live entry prices.");
        W($"  MES leg  ${MesRoundTripUsd}/RT at ${MesPointValue}/pt.   MNQ leg ${MnqRoundTripUsd}/RT at ${MnqPointValue}/pt (primary).");
        W($"  NQ e-mini alternative: ${NqRoundTripUsd}/RT at ${NqPointValue}/pt (secondary column).");

        int[] windows = { 60, 120, 240 };
        double[] thetas = { 1.0, 1.5, 2.0, 2.5, 3.0 };
        const double exitBand = 0.25, stopExtra = 1.5;

        foreach (int wnd in windows)
        {
            W("");
            W($"================  ROLLING WINDOW W = {wnd} H1 bars  ================");
            var zs = RollingZ(spread, wnd);

            // per-theta TRAIN vs OOS
            var grid = new List<(double Theta, int TrN, double TrGross, double TrNetMnq, int OoN, double OoNetMnq, double OoCi, double OoNetNq)>();
            foreach (double th in thetas)
            {
                List<Trade> trades = Simulate(px, spread, zs, wnd, th, exitBand, th + stopExtra, splitBar);
                var tr = trades.Where(t => t.InTrain).ToList();
                var oo = trades.Where(t => !t.InTrain).ToList();
                (double trNet, double _) = MeanCi(tr.Select(t => t.NetRmnq));
                (double trGross, double _2) = MeanCi(tr.Select(t => t.GrossR));
                (double ooNet, double ooCi) = MeanCi(oo.Select(t => t.NetRmnq));
                (double ooNetNq, double _3) = MeanCi(oo.Select(t => t.NetRnq));
                grid.Add((th, tr.Count, trGross, trNet, oo.Count, ooNet, ooCi, ooNetNq));
            }

            W(string.Format("{0,6} {1,8} {2,12} {3,12}   {4,8} {5,14} {6,14}", "theta", "TRAIN n", "TRAIN grossR", "TRAIN netR", "OOS n", "OOS netR(MNQ)", "OOS netR(NQ)"));
            W(new string('-', 92));
            foreach (var g in grid)
                W(string.Format(CultureInfo.InvariantCulture, "{0,6:0.0} {1,8} {2,12:+0.000;-0.000} {3,12:+0.000;-0.000}   {4,8} {5,8:+0.000;-0.000} +/-{6:0.000} {7,10:+0.000;-0.000}",
                    g.Theta, g.TrN, g.TrGross, g.TrNetMnq, g.OoN, g.OoNetMnq, g.OoCi, g.OoNetNq));

            // TRAIN-selected theta (max TRAIN netR with >= 30 train trades), then report its OOS
            var pick = grid.Where(g => g.TrN >= 30).OrderByDescending(g => g.TrNetMnq).FirstOrDefault();
            if (pick.TrN == 0) { W("  no theta had >=30 TRAIN trades — window inconclusive."); continue; }
            string verdict = pick.OoNetMnq - pick.OoCi > 0 ? "NET+ ROBUST"
                : pick.OoNetMnq + pick.OoCi < 0 ? "net- robust" : "straddles 0";
            W($"  -> TRAIN-selected theta = {pick.Theta:0.0}  (TRAIN netR {pick.TrNetMnq:+0.000;-0.000}).  " +
              $"OOS netR(MNQ) = {pick.OoNetMnq:+0.000;-0.000} +/-{pick.OoCi:0.000}  [{verdict}]   OOS netR(NQ e-mini) = {pick.OoNetNq:+0.000;-0.000}");
        }

        WriteReport("qde021_axisB_pairtrading.txt");
        Assert.True(px.Count > 2000);
    }

    /// <summary>Causal rolling z: mean/sd over [t-W, t-1], deviation of spread[t]. NaN for t &lt; W.</summary>
    private static double[] RollingZ(double[] s, int w)
    {
        var z = new double[s.Length];
        for (int t = 0; t < s.Length; t++)
        {
            if (t < w) { z[t] = double.NaN; continue; }
            double sum = 0, sq = 0;
            for (int k = t - w; k < t; k++) { sum += s[k]; sq += s[k] * s[k]; }
            double mu = sum / w;
            double var = Math.Max(0, sq / w - mu * mu);
            double sd = Math.Sqrt(var);
            z[t] = sd > 1e-12 ? (s[t] - mu) / sd : double.NaN;
        }
        return z;
    }

    private List<Trade> Simulate(List<Pair> px, double[] spread, double[] z, int w, double theta,
        double exitBand, double stopLevel, int splitBar)
    {
        var trades = new List<Trade>();
        int i = w;
        while (i < px.Count - 1)
        {
            double zi = z[i];
            if (double.IsNaN(zi) || Math.Abs(zi) < theta || !InEntryWindow(px[i].Et))
            {
                i++;
                continue;
            }

            int entry = i;
            int sign = Math.Sign(zi);               // z>0 => MES rich => short spread (profit if spread falls)
            double entrySpread = spread[entry];
            // sd at entry (reconstruct from window)
            double sum = 0, sq = 0;
            for (int k = entry - w; k < entry; k++) { sum += spread[k]; sq += spread[k] * spread[k]; }
            double mu = sum / w;
            double entrySd = Math.Sqrt(Math.Max(1e-24, sq / w - mu * mu));

            // walk forward to the mandatory-flat bar
            int j = entry + 1;
            DateTime entryEtDate = px[entry].Et.Date;
            while (j < px.Count)
            {
                DateTime et = px[j].Et;
                bool sessionOver = et.Date != entryEtDate || EtMinutes(et) >= RthClose;
                double zj = z[j];
                bool meanTouch = !double.IsNaN(zj) && Math.Abs(zj) <= exitBand;
                bool stop = !double.IsNaN(zj) && Math.Abs(zj) >= stopLevel;
                if (sessionOver || meanTouch || stop) break;
                j++;
            }
            if (j >= px.Count) break;
            int exit = j;

            double pnlSpread = -sign * (spread[exit] - entrySpread);   // reversion capture in log units
            double grossR = pnlSpread / entrySd;

            double costMnq = MesRoundTripUsd / (MesPointValue * px[entry].PMes) + MnqRoundTripUsd / (MnqPointValue * px[entry].PNq);
            double costNq = MesRoundTripUsd / (MesPointValue * px[entry].PMes) + NqRoundTripUsd / (NqPointValue * px[entry].PNq);
            double netRmnq = (pnlSpread - costMnq) / entrySd;
            double netRnq = (pnlSpread - costNq) / entrySd;

            bool inTrain = entry < splitBar - PurgeBarsH1;
            bool inOos = entry >= splitBar;
            if (inTrain || inOos)
                trades.Add(new Trade(entry, grossR, netRmnq, netRnq, entrySd, inTrain));

            i = exit + 1;   // no overlapping positions
        }
        return trades;
    }

    private static bool InEntryWindow(DateTime et)
    {
        if (et.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        int m = EtMinutes(et);
        return m >= RthOpen && m < 15 * 60;   // leave >= 1h to the forced 16:00 flat
    }

    private static List<Pair> JoinPairs(IReadOnlyList<HistoricalBar> mes, IReadOnlyList<HistoricalBar> nq)
    {
        var byTs = new Dictionary<long, decimal>();
        foreach (var b in nq) byTs[b.Timestamp.Ticks] = b.Close;
        long tol = TimeSpan.FromMinutes(5).Ticks;
        var nqSorted = nq.Select(b => b.Timestamp.Ticks).OrderBy(x => x).ToArray();
        var outp = new List<Pair>(mes.Count);
        foreach (var b in mes)
        {
            long k = b.Timestamp.Ticks;
            decimal? nqClose = null;
            if (byTs.TryGetValue(k, out decimal exact)) nqClose = exact;
            else
            {
                int idx = Array.BinarySearch(nqSorted, k);
                if (idx < 0) idx = ~idx;
                foreach (int cand in new[] { idx - 1, idx })
                    if (cand >= 0 && cand < nqSorted.Length && Math.Abs(nqSorted[cand] - k) <= tol)
                    { nqClose = byTs[nqSorted[cand]]; break; }
            }
            if (nqClose is decimal c && c > 0 && b.Close > 0)
            {
                DateTime utc = b.Timestamp;
                outp.Add(new Pair(utc, ToEt(utc), (double)b.Close, (double)c));
            }
        }
        return outp;
    }

    // ── shared stats helpers ────────────────────────────────────────────────────────────────────────
    private static (double mean, double var, int n) MVN(IReadOnlyList<double> v)
    {
        int n = v.Count;
        double m = 0; foreach (double x in v) m += x; m /= n;
        double s = 0; foreach (double x in v) s += (x - m) * (x - m);
        return (m, n > 1 ? s / (n - 1) : 0.0, n);
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

    /// <summary>Holm-Bonferroni step-down. Returns the reject/accept flag per input index.</summary>
    private static bool[] Holm(double[] p, double alpha)
    {
        int m = p.Length;
        var order = Enumerable.Range(0, m).OrderBy(i => p[i]).ToArray();
        var sig = new bool[m];
        bool stillRejecting = true;
        for (int rank = 0; rank < m; rank++)
        {
            int idx = order[rank];
            double crit = alpha / (m - rank);
            if (stillRejecting && p[idx] <= crit) sig[idx] = true;
            else { stillRejecting = false; sig[idx] = false; }
        }
        return sig;
    }

    private static double NormCdf(double x)
    {
        double t = 1.0 / (1.0 + 0.2316419 * Math.Abs(x));
        double poly = t * (0.319381530 + t * (-0.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
        double d = Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI);
        double cdf = 1.0 - d * poly;
        return x < 0 ? 1.0 - cdf : cdf;
    }

    private void WriteReport(string file)
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) return;
        string od = Path.Combine(dir, "Research", "CalendarPairTrading", "Output");
        Directory.CreateDirectory(od);
        File.WriteAllText(Path.Combine(od, file), string.Join("\n", _log) + "\n");
    }
}
