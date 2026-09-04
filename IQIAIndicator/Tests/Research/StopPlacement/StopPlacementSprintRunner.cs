using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Engine.TradePlan;
using Xunit;

namespace IQIAIndicator.Tests.Research.StopPlacement;

/// <summary>
/// Research 2026-08-30. Hypothesis under test (user): "the problem is the stop-loss placement".
/// KEEP the reward:risk ratio fixed at 1.5 (TakeProfit = entry +/- 1.5 x stopDistance) and instead
/// sweep ALTERNATIVE methods for placing the stop, for BOTH tradeable regimes (MeanReverting and
/// Trending), to see whether a better stop makes the 1.5R target reachable with positive expectancy.
///
/// METHOD (same reduced-model discipline as TrendingCalibrationSprintRunner):
///  - The full production pipeline runs ONCE per market (MinRiskReward gate OFF so every directional
///    bar is captured). For each bar the DecisionEngine arbitrated to MeanReverting or Trending with a
///    BUY/SELL candidate and ambiguity below the gate, we take (barIndex, regime, isBuy) and the
///    pipeline's own volatility stop distance as the "A: current" baseline.
///  - For every candidate stop method we recompute stopDistance from the OHLC history known AT the
///    signal bar, set entry = next bar Open (Lot 14.10 fill), TP = entry +/- 1.5 x stopDistance, then
///    simulate the intrabar SL/TP race over <see cref="HorizonBars"/> bars (ambiguous bar -> stop,
///    conservative), else exit at the horizon close. Expectancy is measured per trade in R
///    (grossPriceMove / stopDistance) -> size-independent. At 1% risk ($250/trade) the dollar
///    expectancy per trade is simply expectancyR x 250.
///  - Splits: CROSS-MARKET TRAIN = {NQ, RTY, GC}, OOS = {ES, YM, CL}. TEMPORAL (per symbol, purged
///    70/30 with a <see cref="PurgeBars"/> gap).
///
/// DATA LIMIT: ~59 calendar days per market (shared with the TrendingCalibration _cache). Provisional.
/// Output: Output/grid.csv + Output/summary.txt.
/// </summary>
public sealed class StopPlacementSprintRunner
{
    private const int WarmupBars = 128;
    private const int HorizonBars = 10;
    private const int HistoryWindow = 500;
    private const int AtrWindow = 14;
    private const double RewardRisk = 1.5;              // FIXED - the user's constraint
    private const decimal RiskDollars = 250m;           // 1% of 25 000, for the $/trade view
    private const int PurgeBars = 100;
    private const double TrainFraction = 0.70;
    private const int MinTrades = 10;
    private static readonly double AmbiguityGate = EntryTriggerBuilder.AmbiguityGateThreshold;

    private static readonly string[] TrainSymbols = { "NQ", "RTY", "GC" };
    private static readonly string[] OosSymbols = { "ES", "YM", "CL" };

    private static readonly Dictionary<string, (decimal Tick, decimal PointValue)> Specs = new()
    {
        ["ES"] = (0.25m, 50m),
        ["NQ"] = (0.25m, 20m),
        ["YM"] = (1.00m, 5m),
        ["RTY"] = (0.10m, 50m),
        ["GC"] = (0.10m, 100m),
        ["CL"] = (0.01m, 1000m),
    };

    private readonly ITestOutputHelper _output;
    public StopPlacementSprintRunner(ITestOutputHelper output) => _output = output;

    private readonly record struct Signal(int Bar, MarketState Regime, bool IsBuy, double BaselineStopDist);

    private sealed record SliceMetrics(int Trades, int Wins, double SumR)
    {
        public double WinRate => Trades == 0 ? 0 : Wins / (double)Trades;
        public double ExpectancyR => Trades == 0 ? 0 : SumR / Trades;
    }

    /// <summary>A stop-placement method: name + a function returning the stop DISTANCE (price units,
    /// &gt; 0) from the OHLC history at the signal bar, or null when it cannot be formed.</summary>
    private sealed record StopMethod(string Name, Func<IReadOnlyList<HistoricalBar>, int, bool, double, decimal, double?> Distance);

    [Fact]
    public void Run()
    {
        string outDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Research", "StopPlacement", "Output");
        Directory.CreateDirectory(outDir);

        Dictionary<string, HistoricalSeries> series;
        try { series = LoadAllSeries(); }
        catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {ex.GetType().Name}: {ex.Message}");
            return;
        }

        StopMethod[] methods = BuildMethods();

        // method -> regime -> symbol -> (train, oos-temporal) metrics
        var train = new Dictionary<string, Dictionary<string, Dictionary<string, SliceMetrics>>>();
        var oos = new Dictionary<string, Dictionary<string, Dictionary<string, SliceMetrics>>>();
        foreach (StopMethod m in methods)
        {
            train[m.Name] = new(); oos[m.Name] = new();
            foreach (string reg in new[] { "MeanReverting", "Trending" })
            { train[m.Name][reg] = new(); oos[m.Name][reg] = new(); }
        }

        foreach ((string symbol, HistoricalSeries s) in series)
        {
            List<Signal> signals = DirectionalSignals(s, symbol);
            _output.WriteLine($"[{symbol}] {signals.Count} directional signals " +
                $"(MR {signals.Count(x => x.Regime == MarketState.MeanReverting)}, TR {signals.Count(x => x.Regime == MarketState.Trending)}).");
            int splitBar = (int)(s.Bars.Count * TrainFraction);
            IReadOnlyList<HistoricalBar> b = s.Bars;
            decimal tick = Specs[symbol].Tick;

            foreach (StopMethod method in methods)
            foreach (string reg in new[] { "MeanReverting", "Trending" })
            {
                MarketState want = reg == "MeanReverting" ? MarketState.MeanReverting : MarketState.Trending;
                var trainR = new List<double>();
                var oosR = new List<double>();
                foreach (Signal sig in signals)
                {
                    if (sig.Regime != want) continue;
                    if (!TryTrade(b, sig, method, tick, out double r)) continue;
                    if (sig.Bar < splitBar - PurgeBars) trainR.Add(r);
                    else if (sig.Bar >= splitBar) oosR.Add(r);
                }
                train[method.Name][reg][symbol] = Summarize(trainR);
                oos[method.Name][reg][symbol] = Summarize(oosR);
            }
        }

        WriteGrid(Path.Combine(outDir, "grid.csv"), methods, train, oos);
        WriteSummary(Path.Combine(outDir, "summary.txt"), methods, train, oos);
        _output.WriteLine($"Done. {methods.Length} methods x 2 regimes x {series.Count} markets. See {outDir}.");
    }

    // ── Candidate stop-placement methods ─────────────────────────────────────────────────────────
    private static StopMethod[] BuildMethods()
    {
        var list = new List<StopMethod>
        {
            new("A_current_volband", (b, i, buy, baseline, tick) => baseline > 0 ? baseline : null),
        };
        foreach (double k in new[] { 1.0, 1.5, 2.0, 2.5, 3.0 })
            list.Add(new($"B_atr{k:0.0}", (b, i, buy, baseline, tick) =>
            {
                double atr = Atr(b, i);
                return atr > 0 ? k * atr : null;
            }));
        foreach (int m in new[] { 6, 12, 24 })
        foreach (double buf in new[] { 0.0, 0.5 })
            list.Add(new($"C_swing{m}_b{buf:0.0}", (b, i, buy, baseline, tick) =>
            {
                double atr = Atr(b, i);
                if (atr <= 0) return null;
                int lo = Math.Max(0, i - m + 1);
                decimal entry = b[i].Close;
                if (buy)
                {
                    decimal sw = Enumerable.Range(lo, i - lo + 1).Min(j => b[j].Low);
                    double d = (double)(entry - sw) + buf * atr;
                    return d > 0 ? d : null;
                }
                else
                {
                    decimal sw = Enumerable.Range(lo, i - lo + 1).Max(j => b[j].High);
                    double d = (double)(sw - entry) + buf * atr;
                    return d > 0 ? d : null;
                }
            }));
        foreach (int m in new[] { 12, 24 })
        foreach (double k in new[] { 2.5, 3.0 })
            list.Add(new($"D_chand{m}_k{k:0.0}", (b, i, buy, baseline, tick) =>
            {
                double atr = Atr(b, i);
                if (atr <= 0) return null;
                int lo = Math.Max(0, i - m + 1);
                decimal entry = b[i].Close;
                if (buy)
                {
                    decimal hh = Enumerable.Range(lo, i - lo + 1).Max(j => b[j].High);
                    double d = (double)(entry - hh) + k * atr;   // stop below hh - k*ATR
                    return d > 0 ? d : null;
                }
                else
                {
                    decimal ll = Enumerable.Range(lo, i - lo + 1).Min(j => b[j].Low);
                    double d = (double)(ll - entry) + k * atr;
                    return d > 0 ? d : null;
                }
            }));
        foreach (double p in new[] { 0.002, 0.004, 0.006 })
            list.Add(new($"E_pct{p:0.000}", (b, i, buy, baseline, tick) =>
            {
                double d = p * (double)b[i].Close;
                return d > 0 ? d : null;
            }));
        foreach (int m in new[] { 1, 3 })
        foreach (int tb in new[] { 2, 4 })
            list.Add(new($"F_bar{m}_t{tb}", (b, i, buy, baseline, tick) =>
            {
                int lo = Math.Max(0, i - m + 1);
                decimal entry = b[i].Close;
                decimal buf = tb * tick;
                if (buy)
                {
                    decimal ll = Enumerable.Range(lo, i - lo + 1).Min(j => b[j].Low);
                    double d = (double)(entry - ll + buf);
                    return d > 0 ? d : null;
                }
                else
                {
                    decimal hh = Enumerable.Range(lo, i - lo + 1).Max(j => b[j].High);
                    double d = (double)(hh - entry + buf);
                    return d > 0 ? d : null;
                }
            }));
        return list.ToArray();
    }

    // ── Reduced trade model ─────────────────────────────────────────────────────────────────────
    private static bool TryTrade(IReadOnlyList<HistoricalBar> b, Signal sig, StopMethod method, decimal tick, out double r)
    {
        r = 0;
        int bar = sig.Bar;
        if (bar + 1 + HorizonBars >= b.Count) return false;

        double? dist = method.Distance(b, bar, sig.IsBuy, sig.BaselineStopDist, tick);
        if (dist is not double rawDist || !(rawDist > 0)) return false;

        // tick-round the stop distance, same as the production stop model
        decimal stopDist = Math.Round((decimal)rawDist / tick, MidpointRounding.AwayFromZero) * tick;
        if (stopDist <= 0m) stopDist = tick;

        decimal entry = b[bar + 1].Open;
        if (entry <= 0m) return false;

        decimal stopPrice = sig.IsBuy ? entry - stopDist : entry + stopDist;
        decimal tpDist = stopDist * (decimal)RewardRisk;
        decimal tpPrice = sig.IsBuy ? entry + tpDist : entry - tpDist;

        decimal exit = 0m;
        bool resolved = false;
        for (int k = bar + 1; k <= bar + 1 + HorizonBars; k++)
        {
            HistoricalBar mb = b[k];
            bool stopHit = sig.IsBuy ? mb.Low <= stopPrice : mb.High >= stopPrice;
            bool tpHit = sig.IsBuy ? mb.High >= tpPrice : mb.Low <= tpPrice;
            if (stopHit) { exit = stopPrice; resolved = true; break; }       // ambiguous bar -> stop
            if (tpHit) { exit = tpPrice; resolved = true; break; }
        }
        if (!resolved) exit = b[bar + 1 + HorizonBars].Close;

        decimal grossMove = sig.IsBuy ? exit - entry : entry - exit;
        r = (double)(grossMove / stopDist);
        return true;
    }

    private static double Atr(IReadOnlyList<HistoricalBar> b, int i)
    {
        int lo = i - AtrWindow + 1;
        if (lo < 1) return 0;
        double sum = 0;
        for (int j = lo; j <= i; j++)
        {
            double h = (double)b[j].High, l = (double)b[j].Low, pc = (double)b[j - 1].Close;
            sum += Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(pc - l)));
        }
        return sum / AtrWindow;
    }

    // ── One pipeline pass -> directional signals for BOTH tradeable regimes ──────────────────────
    private static List<Signal> DirectionalSignals(HistoricalSeries s, string symbol)
    {
        var spec = InstrumentRiskSpecification.FromInstrumentInfo(
            new InstrumentInfo(symbol, Specs[symbol].Tick, 1m, Specs[symbol].PointValue, 2),
            minQuantity: 1, maxQuantity: 50, quantityStep: 1);
        // MinRiskReward = null -> gate OFF: capture every directional bar regardless of its natural R:R.
        var policy = new RiskPolicy(0.01m, null, null, null, null, null, null, null, null, null);
        var scenario = BacktestScenario.Create(
            s, new BacktestWindow(symbol, s.FirstTimestamp, s.LastTimestamp.AddMinutes(1)), 25_000m, spec, policy);

        BacktestSignalPipelineResult result =
            new BacktestEngine().RunSignalPipeline(scenario, WarmupBars, PipelineParameterOverrides.None);

        var outp = new List<Signal>();
        foreach (BacktestSignalResult bar in result.Bars)
        {
            if (bar.Status != BacktestSignalStatus.Ready) continue;
            if (bar.Decision is not { } d) continue;
            if (d.Winner is not (MarketState.MeanReverting or MarketState.Trending)) continue;
            if (d.AmbiguityScore >= AmbiguityGate) continue;
            if (bar.TradePlan is not { EntryPrice: decimal ep, StopLoss: decimal sl } tp) continue;
            bool isBuy = tp.Direction == DirectionCandidate.BUY_CANDIDATE;
            bool isSell = tp.Direction == DirectionCandidate.SELL_CANDIDATE;
            if (!isBuy && !isSell) continue;
            outp.Add(new Signal(bar.BarIndex, d.Winner, isBuy, (double)Math.Abs(ep - sl)));
        }
        return outp;
    }

    // ── Reporting ──────────────────────────────────────────────────────────────────────────────
    private static SliceMetrics Summarize(List<double> rs)
    {
        if (rs.Count == 0) return new SliceMetrics(0, 0, 0);
        return new SliceMetrics(rs.Count, rs.Count(x => x >= 0), rs.Sum());
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] v = values.OrderBy(x => x).ToArray();
        if (v.Length == 0) return double.NaN;
        return v.Length % 2 == 1 ? v[v.Length / 2] : 0.5 * (v[v.Length / 2 - 1] + v[v.Length / 2]);
    }

    private void WriteSummary(string path, StopMethod[] methods,
        Dictionary<string, Dictionary<string, Dictionary<string, SliceMetrics>>> train,
        Dictionary<string, Dictionary<string, Dictionary<string, SliceMetrics>>> oos)
    {
        var lines = new List<string>
        {
            $"STOP-PLACEMENT SWEEP (R:R fixed at {RewardRisk}) - {DateTime.UtcNow:O}",
            $"6 markets M5 ~59d. TRAIN={string.Join(",", TrainSymbols)} OOS={string.Join(",", OosSymbols)}. " +
            $"Horizon {HorizonBars} bars. Metric = expectancy/trade in R; $/trade at 1% risk = R x {RiskDollars:0}.",
            $"Ambiguity gate {AmbiguityGate:0.00}. MinTrades/symbol for the median = {MinTrades}.",
            "",
        };

        foreach (string reg in new[] { "MeanReverting", "Trending" })
        {
            lines.Add($"================  {reg}  ================");
            lines.Add("method                trainMedR  $/trade   oosMktMedR  oosTempMedR  trainSyms  trainTrades");
            var ranked = methods.Select(m =>
            {
                double[] trR = TrainSymbols.Select(sym => train[m.Name][reg][sym]).Where(x => x.Trades >= MinTrades).Select(x => x.ExpectancyR).ToArray();
                double[] mkR = OosSymbols.Select(sym => train[m.Name][reg][sym]).Where(x => x.Trades >= MinTrades).Select(x => x.ExpectancyR).ToArray();
                double[] tpR = TrainSymbols.Concat(OosSymbols).Select(sym => oos[m.Name][reg][sym]).Where(x => x.Trades >= MinTrades / 2).Select(x => x.ExpectancyR).ToArray();
                return new
                {
                    m.Name,
                    TrR = Median(trR),
                    MkR = Median(mkR),
                    TpR = Median(tpR),
                    Syms = trR.Length,
                    Trades = TrainSymbols.Sum(sym => train[m.Name][reg][sym].Trades),
                };
            })
            .OrderByDescending(x => double.IsNaN(x.TrR) ? double.NegativeInfinity : x.TrR)
            .ToList();

            foreach (var x in ranked)
                lines.Add($"{x.Name,-20} {Fmt(x.TrR),9} {(double.IsNaN(x.TrR) ? "     n/a" : $"{x.TrR * (double)RiskDollars,8:F1}")} " +
                          $"{Fmt(x.MkR),11} {Fmt(x.TpR),12} {x.Syms,10} {x.Trades,12}");

            var best = ranked.FirstOrDefault(x => !double.IsNaN(x.TrR) && x.Syms >= 2);
            lines.Add("");
            if (best is null)
                lines.Add($"VERDICT [{reg}]: no method reached {MinTrades} trades on >= 2 TRAIN markets - too sparse.");
            else
            {
                bool a = best.TrR > 0, m2 = best.MkR > 0, t = best.TpR > 0;
                lines.Add($"BEST TRAIN METHOD [{reg}]: {best.Name}  trainMedR={best.TrR:F4} ({(a ? "positive" : "NOT positive")}), " +
                          $"~${best.TrR * (double)RiskDollars:F1}/trade");
                lines.Add($"  OOS-market medR = {best.MkR:F4} ({(m2 ? "holds" : "does NOT hold")}); " +
                          $"OOS-temporal medR = {best.TpR:F4} ({(t ? "holds" : "does NOT hold")})");
                lines.Add(a && m2 && t
                    ? $"  => PROVISIONAL: '{best.Name}' gives {reg} a positive 1.5R expectancy on TRAIN and both OOS checks (~59d => provisional)."
                    : $"  => NO ROBUST METHOD for {reg}: changing the stop alone does not make a fixed 1.5R target profitable on this data.");
            }
            lines.Add("");
        }

        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        foreach (string l in lines) _output.WriteLine(l);

        static string Fmt(double d) => double.IsNaN(d) ? "n/a" : d.ToString("F4", CultureInfo.InvariantCulture);
    }

    private static void WriteGrid(string path, StopMethod[] methods,
        Dictionary<string, Dictionary<string, Dictionary<string, SliceMetrics>>> train,
        Dictionary<string, Dictionary<string, Dictionary<string, SliceMetrics>>> oos)
    {
        var sb = new List<string> { "method,regime,symbol,slice,trades,winRate,expectancyR,sumR" };
        foreach (StopMethod m in methods)
        foreach (string reg in new[] { "MeanReverting", "Trending" })
        {
            foreach ((string sym, SliceMetrics mm) in train[m.Name][reg]) sb.Add(Row(m.Name, reg, sym, "train", mm));
            foreach ((string sym, SliceMetrics mm) in oos[m.Name][reg]) sb.Add(Row(m.Name, reg, sym, "oosTemporal", mm));
        }
        File.WriteAllText(path, string.Join("\n", sb) + "\n");

        static string Row(string method, string reg, string sym, string slice, SliceMetrics m) => string.Join(",",
            method, reg, sym, slice, m.Trades,
            m.WinRate.ToString("0.000", CultureInfo.InvariantCulture),
            m.ExpectancyR.ToString("0.0000", CultureInfo.InvariantCulture),
            m.SumR.ToString("0.00", CultureInfo.InvariantCulture));
    }

    // ── Data (shares the TrendingCalibration daily _cache; no extra Yahoo hit if already pulled) ──
    private Dictionary<string, HistoricalSeries> LoadAllSeries()
    {
        string cacheDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Research",
            "TrendingCalibration", "_cache", DateTime.UtcNow.ToString("yyyyMMdd"));
        Directory.CreateDirectory(cacheDir);

        var source = new YahooHistoricalBarSource();
        DateTime to = DateTime.UtcNow;
        DateTime from = to.AddDays(-YahooHistoricalBarSource.DefaultMaxChunkSpanDays);
        var result = new Dictionary<string, HistoricalSeries>();
        foreach (string sym in TrainSymbols.Concat(OosSymbols))
        {
            string cachePath = Path.Combine(cacheDir, $"{sym}.csv");
            if (File.Exists(cachePath))
            {
                result[sym] = ReadCache(sym, cachePath);
                _output.WriteLine($"[{sym}] {result[sym].Bars.Count} bars from cache.");
                continue;
            }
            HistoricalSeries s = source.Load(sym, "M5", from, to, CancellationToken.None);
            WriteCache(cachePath, s);
            result[sym] = s;
            _output.WriteLine($"[{sym}] downloaded {s.Bars.Count} bars.");
        }
        return result;
    }

    private static void WriteCache(string path, HistoricalSeries s)
    {
        var sb = new List<string>(s.Bars.Count + 1) { "ts,o,h,l,c,v" };
        foreach (HistoricalBar b in s.Bars)
            sb.Add(string.Join(",", b.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                b.Open.ToString(CultureInfo.InvariantCulture), b.High.ToString(CultureInfo.InvariantCulture),
                b.Low.ToString(CultureInfo.InvariantCulture), b.Close.ToString(CultureInfo.InvariantCulture),
                b.Volume.ToString(CultureInfo.InvariantCulture)));
        File.WriteAllText(path, string.Join("\n", sb) + "\n");
    }

    private static HistoricalSeries ReadCache(string symbol, string path)
    {
        var bars = new List<HistoricalBar>();
        foreach (string line in File.ReadLines(path).Skip(1))
        {
            string[] p = line.Split(',');
            if (p.Length < 6) continue;
            bars.Add(new HistoricalBar(
                DateTime.Parse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                decimal.Parse(p[1], CultureInfo.InvariantCulture), decimal.Parse(p[2], CultureInfo.InvariantCulture),
                decimal.Parse(p[3], CultureInfo.InvariantCulture), decimal.Parse(p[4], CultureInfo.InvariantCulture),
                decimal.Parse(p[5], CultureInfo.InvariantCulture)));
        }
        return HistoricalSeries.Create(symbol, "M5", "UTC", "Yahoo(Continuous)-cached", bars);
    }
}
