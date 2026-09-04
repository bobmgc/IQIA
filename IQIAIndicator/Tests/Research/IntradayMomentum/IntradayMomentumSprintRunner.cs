using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace IQIAIndicator.Tests.Research.IntradayMomentum;

/// <summary>
/// Research 2026-08-30. Standalone reproduction of the intraday time-series momentum strategy of
/// Zarattini, Aziz and Barbon, "Beat the Market: An Effective Intraday Momentum Strategy for S&P500
/// ETF (SPY)" (SSRN 4824172). DELIBERATELY OUTSIDE the IQIA regime->methodology->model pipeline: this
/// strategy is anchored to the session clock (a time-of-day volatility envelope around the RTH open),
/// not to a "regime".
///
/// DATA: SPY RTH 1-minute bars, 2007-2025, from London Strategic Edge (/v1/candles), cached to
/// scratchpad/lse_cache/SPY_1m_YYYY.csv by the pull step. Matches the paper's SPY/1-min/IQFeed setup.
///
/// STRATEGY (paper Section 3, "Curr.Band + VWAP" trailing stop, CONSTANT size / NO leverage - the
/// honest core, ~Sharpe 1.24 / +380% in the paper's Table 2):
///  - Noise Area on day t: for each minute-of-session k, sigma_t[k] = mean over the last
///    <see cref="Lookback"/> trading days of |Close_{t-i}[k] / Open_{t-i,open} - 1|.
///    Upper[k] = max(Open_t, PrevClose) * (1 + VM*sigma_t[k]);
///    Lower[k] = min(Open_t, PrevClose) * (1 - VM*sigma_t[k]).   VM = <see cref="VolatilityMultiplier"/>.
///  - Decisions ONLY at :00 and :30, from 10:00 to 15:30 inclusive (paper: "trading is restricted to
///    semi-hourly intervals ... takes its first position at 10:00"). Stops are also evaluated only at
///    those marks.
///  - Flat  -> price above Upper[k] => long; below Lower[k] => short.
///  - Long  -> price <= Lower[k]              => close & reverse to short;
///             price <= max(Upper[k], VWAP[k]) => close, go flat;  (mirror for short)
///  - Any open position is closed at the last bar of the session (EOD flat).
///  - Size: shares = floor(AUM_start_of_day / Open_t), held constant for the day.
///  - Costs: <see cref="CommissionPerShare"/> + <see cref="SlippagePerShare"/> on every entry AND exit.
///
/// SPLITS reported separately: TRAIN 2007-01-01..2019-12-31, OOS 2020-01-01..2024-05-10 (paper's first
/// version), POST-PUBLICATION 2024-05-10..end (a genuine blind out-of-sample). Also a SPY buy&amp;hold
/// reference. Output: Output/summary.txt + Output/equity.csv.
/// </summary>
public sealed class IntradayMomentumSprintRunner
{
    private const int Lookback = 14;
    private const double VolatilityMultiplier = 1.0;
    private const decimal CommissionPerShare = 0.0035m;
    private const decimal SlippagePerShare = 0.001m;
    private const decimal InitialCapital = 100_000m;

    private static readonly DateTime TrainEnd = new(2019, 12, 31);
    private static readonly DateTime OosEnd = new(2024, 5, 10);   // paper's "First Version: May 10, 2024"

    private readonly ITestOutputHelper _output;
    public IntradayMomentumSprintRunner(ITestOutputHelper output) => _output = output;

    private static string CacheDir => Path.Combine(
        "C:", "Users", "rnbch", "AppData", "Local", "Temp", "claude",
        "c--Users-rnbch-OneDrive-Bureau-IQIA", "4ed24526-4bae-4588-9970-cec462f9a809", "scratchpad", "lse_cache");

    private readonly record struct Bar(DateTime Ts, double O, double H, double L, double C, double V);

    private sealed class Day
    {
        public DateTime Date;
        public List<Bar> Bars = new();
        public double Open;                 // first bar Open
        public double PrevClose;
        public int[] MinuteIndex = Array.Empty<int>();   // minutes since session open, per bar
        public double[] Vwap = Array.Empty<double>();    // running RTH VWAP, per bar
    }

    private sealed record SplitStats(string Name, int Days, int ActiveDays, double TotalReturn, double Cagr,
        double AnnVol, double Sharpe, double HitRatio, double MaxDrawdown, double Skew, double WorstDay,
        double BestDay, int Trades);

    [Fact]
    public void Run()
    {
        string outDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Research", "IntradayMomentum", "Output");
        Directory.CreateDirectory(outDir);

        if (!Directory.Exists(CacheDir) || !Directory.EnumerateFiles(CacheDir, "SPY_1m_*.csv").Any())
        {
            Assert.Skip($"SPY 1-minute cache not found at {CacheDir} - run the LSE pull step first.");
            return;
        }

        List<Day> days = LoadDays();
        _output.WriteLine($"Loaded {days.Count} trading days, {days.First().Date:yyyy-MM-dd}..{days.Last().Date:yyyy-MM-dd}.");

        var equity = Simulate(days, InitialCapital);
        decimal aum = equity[^1].Aum;
        int totalTrades = equity.Sum(x => x.Trades);

        // ── Metrics per split ──────────────────────────────────────────────────────────────────────
        var lines = new List<string>
        {
            $"INTRADAY MOMENTUM (Zarattini/Aziz/Barbon) - SPY 1-min RTH - {DateTime.UtcNow:O}",
            $"Config: Lookback={Lookback}, VM={VolatilityMultiplier}, size=constant/no-leverage, " +
            $"cost={CommissionPerShare}+{SlippagePerShare} $/share per side, start=${InitialCapital:N0}.",
            $"Decisions at :00/:30 from 10:00 to 15:30; trailing stop = max(band, VWAP); flat at close.",
            $"Total trades: {totalTrades}. Final equity: ${aum:N0}  (x{aum / InitialCapital:F2}).",
            "",
            "SPLIT          days  active  trades  totRet%   CAGR%   annVol%  Sharpe  hit%   MDD%   skew  worst%  best%",
        };
        foreach (SplitStats s in new[]
        {
            Metrics("FULL 2007-2025", equity, DateTime.MinValue, DateTime.MaxValue),
            Metrics("TRAIN 07-19", equity, DateTime.MinValue, TrainEnd),
            Metrics("OOS 20-24May", equity, TrainEnd.AddDays(1), OosEnd),
            Metrics("POSTPUB 24-25", equity, OosEnd.AddDays(1), DateTime.MaxValue),
        })
        {
            lines.Add($"{s.Name,-14} {s.Days,5} {s.ActiveDays,7} {s.Trades,7} {s.TotalReturn * 100,8:F1} {s.Cagr * 100,7:F1} " +
                      $"{s.AnnVol * 100,8:F1} {s.Sharpe,7:F2} {s.HitRatio * 100,5:F0} {s.MaxDrawdown * 100,6:F1} " +
                      $"{s.Skew,6:F2} {s.WorstDay * 100,6:F1} {s.BestDay * 100,6:F1}");
        }

        // Buy & hold SPY reference over the same window
        double bh = days.Last().Bars.Last().C / days.First().Open - 1.0;
        double bhYears = days.Count / 252.0;
        var bhList = new List<double>();
        for (int i = 1; i < days.Count; i++) bhList.Add(days[i].Bars.Last().C / days[i - 1].Bars.Last().C - 1.0);
        double[] bhDaily = bhList.ToArray();
        double bhSharpe = Mean(bhDaily) / StdDev(bhDaily) * Math.Sqrt(252);
        lines.Add("");
        lines.Add($"SPY BUY&HOLD    totRet%={bh * 100:F0}   CAGR%={(Math.Pow(1 + bh, 1 / bhYears) - 1) * 100:F1}   " +
                  $"annVol%={StdDev(bhDaily) * Math.Sqrt(252) * 100:F1}   Sharpe={bhSharpe:F2}   MDD%={MaxDd(bhDaily) * 100:F0}");
        lines.Add("");
        lines.Add("Paper (Curr.Band+VWAP, constant size, Table 2): totRet 380%, CAGR 9.7%, annVol 7.7%, Sharpe 1.24, hit 43%, MDD 12%.");
        lines.Add("Paper (same + vol-target 2% & up-to-4x leverage): totRet 1985%, CAGR 19.6%, Sharpe 1.33, MDD 25%.");

        File.WriteAllText(Path.Combine(outDir, "summary.txt"), string.Join("\n", lines) + "\n");
        using (var w = new StreamWriter(Path.Combine(outDir, "equity.csv")))
        {
            w.WriteLine("date,aum,dailyReturn,trades");
            foreach ((DateTime dt, decimal a, double r, int tr) in equity)
                w.WriteLine($"{dt:yyyy-MM-dd},{a.ToString(CultureInfo.InvariantCulture)},{r.ToString("G17", CultureInfo.InvariantCulture)},{tr}");
        }
        foreach (string l in lines) _output.WriteLine(l);
    }

    /// <summary>
    /// Three-strategy combined test over the SAME recent ~45-day window the IQIA-pipeline MES backtest
    /// used (2026-07-14 .. 2026-08-28), one 25 000 $ sleeve each. The MeanReverting / Trending numbers
    /// are the constants captured from that run; this fact only computes the intraday-momentum SPY
    /// sleeve on the identical calendar window (2026 SPY 1-min cache, warm-up bars before the window
    /// build sigma but accrue no PnL), then reports the equal-weight portfolio.
    /// </summary>
    [Fact]
    public void RunThreeStrategyCommonWindow()
    {
        string outDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Research", "IntradayMomentum", "Output");
        Directory.CreateDirectory(outDir);
        string cache2026 = Path.Combine(CacheDir, "SPY_1m_2026.csv");
        if (!File.Exists(cache2026)) { Assert.Skip($"{cache2026} not found - pull SPY 1-min 2026 first."); return; }

        var winFrom = new DateTime(2026, 7, 14);
        var winTo = new DateTime(2026, 8, 28);

        List<Day> days = LoadDays(new[] { cache2026 });
        var full = Simulate(days, InitialCapital);                       // 100k, whole 2026 (warm-up incl.)
        var win = full.Where(x => x.Date >= winFrom && x.Date <= winTo).ToList();
        if (win.Count < 2) { Assert.Skip("Common window has too few days in the 2026 cache."); return; }

        // rescale the 100k-sleeve daily returns onto a fresh 25 000 $ sleeve, compounded
        decimal spyCap = 25_000m, spyPeak = 25_000m, spyMaxDd = 0m;
        int spyTrades = 0, spyPosDays = 0;
        foreach (var row in win)
        {
            spyCap *= (decimal)(1.0 + row.Ret);
            if (spyCap > spyPeak) spyPeak = spyCap;
            decimal dd = spyCap - spyPeak;
            if (dd < spyMaxDd) spyMaxDd = dd;
            spyTrades += row.Trades;
            if (row.Ret > 0) spyPosDays++;
        }
        decimal spyNet = spyCap - 25_000m;

        // constants captured from the IQIA-pipeline MES=F M5 run over the same window (pnl_both.txt)
        const decimal mrNet = 592.55m; const int mrTrades = 3;
        const decimal trNet = -7088.75m; const int trTrades = 198;

        decimal combinedNet = mrNet + trNet + spyNet;
        var lines = new List<string>
        {
            $"THREE-STRATEGY COMBINED - common window {winFrom:yyyy-MM-dd} .. {winTo:yyyy-MM-dd} - {DateTime.UtcNow:O}",
            "Each sleeve on its own 25 000 $. MR/Trending: IQIA pipeline MES=F M5, 1% risk/trade, R:R>=1.5 (from pnl_both.txt).",
            "Intraday-momentum: standalone SPY 1-min harness, constant no-leverage size, cost 0.0035+0.001 $/sh per side.",
            "",
            $"[MeanReverting  ] trades={mrTrades,4}  NET={mrNet,12:F2}  finalEquity={25_000m + mrNet,12:F2}",
            $"[Trending       ] trades={trTrades,4}  NET={trNet,12:F2}  finalEquity={25_000m + trNet,12:F2}",
            $"[IntradayMomSPY ] trades={spyTrades,4}  NET={spyNet,12:F2}  finalEquity={spyCap,12:F2}  " +
            $"posDays={spyPosDays}/{win.Count}  maxDD={spyMaxDd:F2}",
            "",
            $"[PORTFOLIO 3x25k] startingCapital=75 000.00  NET={combinedNet,12:F2}  " +
            $"finalEquity={75_000m + combinedNet:F2}  return={(double)(combinedNet / 75_000m) * 100:F1}%",
            "",
            $"SPY intraday sleeve day-by-day return series over the window ({win.Count} days):",
        };
        foreach (var row in win)
            lines.Add($"  {row.Date:yyyy-MM-dd}  ret={row.Ret * 100,7:F3}%  trades={row.Trades}");

        File.WriteAllText(Path.Combine(outDir, "three_strategy.txt"), string.Join("\n", lines) + "\n");
        foreach (string l in lines) _output.WriteLine(l);
    }

    // ── Simulation driver ─────────────────────────────────────────────────────────────────────────
    private static List<(DateTime Date, decimal Aum, double Ret, int Trades)> Simulate(List<Day> days, decimal capital)
    {
        var equity = new List<(DateTime Date, decimal Aum, double Ret, int Trades)>();
        decimal aum = capital;
        var moveHist = new Dictionary<int, Queue<double>>();     // minuteIndex -> last Lookback |move|

        for (int di = 0; di < days.Count; di++)
        {
            Day d = days[di];

            var sigma = new Dictionary<int, double>();
            foreach ((int k, Queue<double> q) in moveHist)
            {
                if (q.Count >= Lookback) sigma[k] = q.Average();
            }

            (decimal dayPnl, int trades) = SimulateDay(d, sigma, aum);
            double ret = aum == 0m ? 0.0 : (double)(dayPnl / aum);
            aum += dayPnl;
            equity.Add((d.Date, aum, ret, trades));

            for (int b = 0; b < d.Bars.Count; b++)
            {
                int k = d.MinuteIndex[b];
                double move = Math.Abs(d.Bars[b].C / d.Open - 1.0);
                if (!moveHist.TryGetValue(k, out Queue<double>? q)) { q = new Queue<double>(); moveHist[k] = q; }
                q.Enqueue(move);
                while (q.Count > Lookback) q.Dequeue();
            }
        }
        return equity;
    }

    // ── One session ───────────────────────────────────────────────────────────────────────────────
    private static (decimal Pnl, int Trades) SimulateDay(Day d, Dictionary<int, double> sigma, decimal aumStart)
    {
        if (sigma.Count == 0 || d.Bars.Count < 30) return (0m, 0);

        double upperAnchor = Math.Max(d.Open, d.PrevClose);
        double lowerAnchor = Math.Min(d.Open, d.PrevClose);
        int shares = (int)Math.Floor(aumStart / (decimal)d.Open);
        if (shares <= 0) return (0m, 0);

        // index by minute-of-session for O(1) lookup at decision points
        var byIdx = new Dictionary<int, int>();     // minuteIndex -> bar position
        for (int b = 0; b < d.Bars.Count; b++) byIdx[d.MinuteIndex[b]] = b;

        int pos = 0;                 // -1 short, 0 flat, +1 long
        double entryPx = 0;
        decimal pnl = 0m;
        int trades = 0;
        decimal costPerTxn = shares * (CommissionPerShare + SlippagePerShare);

        void Close(double px) { pnl += (decimal)(pos * (px - entryPx)) * shares - costPerTxn; pos = 0; trades++; }
        void Open(int side, double px) { pos = side; entryPx = px; pnl -= costPerTxn; trades++; }

        DateTime sessionOpen = d.Bars[0].Ts;
        foreach (int k in DecisionIndices(d, sessionOpen))
        {
            if (!byIdx.TryGetValue(k, out int bpos)) continue;
            if (!sigma.TryGetValue(k, out double sig)) continue;
            double p = d.Bars[bpos].C;
            double ub = upperAnchor * (1 + VolatilityMultiplier * sig);
            double lb = lowerAnchor * (1 - VolatilityMultiplier * sig);
            double vwap = d.Vwap[bpos];

            if (pos == 1)
            {
                if (p <= lb) { Close(p); Open(-1, p); }
                else if (p <= Math.Max(ub, vwap)) Close(p);
            }
            else if (pos == -1)
            {
                if (p >= ub) { Close(p); Open(1, p); }
                else if (p >= Math.Min(lb, vwap)) Close(p);
            }
            else
            {
                if (p > ub) Open(1, p);
                else if (p < lb) Open(-1, p);
            }
        }

        if (pos != 0) Close(d.Bars[^1].C);      // EOD flat
        return (pnl, trades);
    }

    private static IEnumerable<int> DecisionIndices(Day d, DateTime sessionOpen)
    {
        // :00 and :30 marks, 10:00..15:30 inclusive, expressed as minutes since session open
        foreach (Bar b in d.Bars)
        {
            // convert to ET clock
            DateTime et = ToEastern(b.Ts);
            if (et.Minute != 0 && et.Minute != 30) continue;
            var tod = et.TimeOfDay;
            if (tod < new TimeSpan(10, 0, 0) || tod > new TimeSpan(15, 30, 0)) continue;
            yield return (int)Math.Round((b.Ts - sessionOpen).TotalMinutes);
        }
    }

    // ── Loading ───────────────────────────────────────────────────────────────────────────────────
    private static List<Day> LoadDays(IEnumerable<string>? files = null)
    {
        var all = new List<Bar>();
        foreach (string f in (files ?? Directory.EnumerateFiles(CacheDir, "SPY_1m_*.csv")).OrderBy(x => x))
        {
            foreach (string line in File.ReadLines(f).Skip(1))
            {
                int c1 = line.IndexOf(','); if (c1 < 0) continue;
                DateTime ts = DateTime.Parse(line.AsSpan(0, c1), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                string[] p = line.Substring(c1 + 1).Split(',');
                all.Add(new Bar(ts,
                    double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture),
                    double.Parse(p[2], CultureInfo.InvariantCulture), double.Parse(p[3], CultureInfo.InvariantCulture),
                    double.Parse(p[4], CultureInfo.InvariantCulture)));
            }
        }
        all.Sort((a, b) => a.Ts.CompareTo(b.Ts));

        var days = new List<Day>();
        Day? cur = null;
        DateTime curEtDate = default;
        foreach (Bar b in all)
        {
            DateTime etDate = ToEastern(b.Ts).Date;
            if (cur is null || etDate != curEtDate)
            {
                cur = new Day { Date = etDate };
                curEtDate = etDate;
                days.Add(cur);
            }
            cur.Bars.Add(b);
        }

        // per-day derived fields
        for (int i = 0; i < days.Count; i++)
        {
            Day d = days[i];
            d.Open = d.Bars[0].O;
            d.PrevClose = i > 0 ? days[i - 1].Bars[^1].C : d.Bars[0].O;
            DateTime so = d.Bars[0].Ts;
            d.MinuteIndex = d.Bars.Select(x => (int)Math.Round((x.Ts - so).TotalMinutes)).ToArray();
            d.Vwap = new double[d.Bars.Count];
            double cpv = 0, cv = 0;
            for (int b = 0; b < d.Bars.Count; b++)
            {
                double typ = (d.Bars[b].H + d.Bars[b].L + d.Bars[b].C) / 3.0;
                cpv += typ * d.Bars[b].V; cv += d.Bars[b].V;
                d.Vwap[b] = cv > 0 ? cpv / cv : typ;
            }
        }

        // drop the warm-up days that can never have a full sigma window
        return days;
    }

    private static readonly TimeZoneInfo Eastern = ResolveEastern();
    private static TimeZoneInfo ResolveEastern()
    {
        foreach (string id in new[] { "Eastern Standard Time", "America/New_York" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        }
        return TimeZoneInfo.Utc;
    }
    private static DateTime ToEastern(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Eastern);

    // ── Stats ─────────────────────────────────────────────────────────────────────────────────────
    private static SplitStats Metrics(string name, List<(DateTime Date, decimal Aum, double Ret, int Trades)> eq,
        DateTime from, DateTime to)
    {
        var slice = eq.Where(x => x.Date >= from && x.Date <= to).ToList();
        int trades = slice.Sum(x => x.Trades);
        if (slice.Count < 2) return new SplitStats(name, slice.Count, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, trades);
        double[] r = slice.Select(x => x.Ret).ToArray();
        int active = r.Count(x => x != 0.0);
        // compound the daily returns for the slice (path-independent of the absolute AUM level)
        double comp = 1.0;
        foreach (double x in r) comp *= 1 + x;
        double total = comp - 1;
        double years = slice.Count / 252.0;
        double cagr = Math.Pow(comp, 1 / years) - 1;
        double vol = StdDev(r) * Math.Sqrt(252);
        double sharpe = StdDev(r) > 0 ? Mean(r) / StdDev(r) * Math.Sqrt(252) : 0;
        double hit = active > 0 ? r.Count(x => x > 0) / (double)active : 0;
        return new SplitStats(name, slice.Count, active, total, cagr, vol, sharpe, hit,
            MaxDd(r), Skew(r), r.Min(), r.Max(), trades);
    }

    private static double Mean(IEnumerable<double> xs) { var a = xs.ToArray(); return a.Length == 0 ? 0 : a.Average(); }
    private static double StdDev(IEnumerable<double> xs)
    {
        var a = xs.ToArray();
        if (a.Length < 2) return 0;
        double m = a.Average();
        return Math.Sqrt(a.Sum(x => (x - m) * (x - m)) / (a.Length - 1));
    }
    private static double Skew(double[] a)
    {
        if (a.Length < 3) return 0;
        double m = a.Average();
        double sd = Math.Sqrt(a.Sum(x => (x - m) * (x - m)) / a.Length);
        if (sd == 0) return 0;
        return a.Sum(x => Math.Pow((x - m) / sd, 3)) / a.Length;
    }
    private static double MaxDd(double[] r)
    {
        double eq = 1, peak = 1, mdd = 0;
        foreach (double x in r) { eq *= 1 + x; peak = Math.Max(peak, eq); mdd = Math.Min(mdd, eq / peak - 1); }
        return mdd;
    }
}
