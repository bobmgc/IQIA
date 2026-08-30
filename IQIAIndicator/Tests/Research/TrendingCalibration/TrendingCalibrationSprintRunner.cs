using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.Trend;
using IQIAIndicator.Engine.EntryTrigger;
using Xunit;

namespace IQIAIndicator.Tests.Research.TrendingCalibration;

/// <summary>
/// Audit 2026-08-30 (P0-2). Trend-following (TimeSeriesMomentumModel) parameter calibration.
///
/// PROTOCOL:
///  - 6 cross-market series (ES/NQ/RTY indices, YM Dow, GC gold, CL crude), M5, ~59 days each, from the
///    same bounded YahooHistoricalBarSource production uses.
///  - The full production pipeline (regime evidence -> Decision) is run ONCE per symbol with default
///    parameters - none of the four calibration knobs can change which bars are Trending, the evidence,
///    or the arbitration, so re-running it per grid cell would be ~80x wasted work. For every Trending
///    Ready bar the grid then only re-evaluates TimeSeriesMomentumModel (the sole thing the lookbacks
///    change), re-applies the momentum-confidence gate, and re-simulates the trade with the cell's
///    stop / take-profit - a faithful, deterministic reduced model of the EntryTrigger + TradePlan +
///    intrabar-ExecutionSimulator path.
///  - Two independent splits, both reported:
///      * CROSS-MARKET: TRAIN = {NQ, RTY, GC}, OOS = {ES, YM, CL} (the non-temporal split the project
///        protocol prefers - never the same market on both sides).
///      * TEMPORAL (per symbol, purged): signal bars before 70% of the series = TRAIN, from 70% = OOS,
///        with a <see cref="PurgeBars"/>-bar gap dropped between them.
///  - Grid: MomentumLookbacks x MinMomentumConfidence x StopVolatilityMultiplier x TakeProfitRMultiple.
///  - Metric per (cell, symbol, slice): TRENDING trades only, expectancy per trade in R units
///    (grossPriceMove / stopDistance), plus trade count and win rate.
///  - Selection: rank cells by MEDIAN cross-market TRAIN expectancy-R (>= <see cref="MinTradesForSymbol"/>
///    trades on >= 2 TRAIN markets), then confirm / reject the winner against the OOS market set AND the
///    temporal OOS slice - never re-selected after either is inspected.
///
/// DATA LIMIT (stated): ~59 calendar days per market. Any selection is PROVISIONAL - a first pass, not a
/// multi-year walk-forward. Output: Output/grid.csv + Output/summary.txt.
/// </summary>
public sealed class TrendingCalibrationSprintRunner
{
    private const int WarmupBars = 128;
    private const int HorizonBars = 10;
    private const int HistoryWindow = 500;          // matches BacktestEngine.BuildScientificMarketContext
    private const decimal RiskPerTrade = 250m;      // 1% of 25 000
    private const int MaxQuantityCap = 50;
    private const double AmbiguityGate = EntryTriggerBuilder.AmbiguityGateThreshold;
    private const int PurgeBars = 100;
    private const double TrainFraction = 0.70;
    private const int MinTradesForSymbol = 12;

    private static readonly string[] TrainSymbols = { "NQ", "RTY", "GC" };
    private static readonly string[] OosSymbols = { "ES", "YM", "CL" };

    private static readonly (int[] Lookbacks, string Label)[] LookbackSets =
    {
        (new[] { 6, 18, 54 }, "short"),
        (new[] { 12, 36, 72, 144 }, "default"),
        (new[] { 24, 72, 216 }, "long"),
    };
    private static readonly double[] MinMomentumConfidences = { 0.05, 0.15, 0.30 };
    private static readonly double[] StopMultipliers = { 1.5, 2.5, 3.5 };
    private static readonly double[] TakeProfitRMultiples = { 1.5, 2.5, 3.5 };

    // tickSize, pointValue per instrument (TickValue/decimals not needed for the reduced model).
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

    public TrendingCalibrationSprintRunner(ITestOutputHelper output) => _output = output;

    private sealed record SliceMetrics(int Trades, int Wins, double SumR)
    {
        public double WinRate => Trades == 0 ? 0 : Wins / (double)Trades;
        public double ExpectancyR => Trades == 0 ? 0 : SumR / Trades;
    }

    private sealed record Cell(string Id, int[] Lookbacks, string LookbackLabel, double MinConf, double StopMult, double TpR)
    {
        public readonly Dictionary<string, SliceMetrics> Train = new();
        public readonly Dictionary<string, SliceMetrics> Oos = new();
    }

    [Fact]
    public void Run()
    {
        string outputDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Research", "TrendingCalibration", "Output");
        Directory.CreateDirectory(outputDir);

        Dictionary<string, HistoricalSeries> series;
        try { series = LoadAllSeries(); }
        catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var cells = new List<Cell>();
        foreach ((int[] lb, string label) in LookbackSets)
        foreach (double mc in MinMomentumConfidences)
        foreach (double sl in StopMultipliers)
        foreach (double tp in TakeProfitRMultiples)
        {
            string id = string.Format(CultureInfo.InvariantCulture, "{0}|mc{1:0.00}|sl{2:0.0}|tp{3:0.0}", label, mc, sl, tp);
            cells.Add(new Cell(id, lb, label, mc, sl, tp));
        }

        foreach ((string symbol, HistoricalSeries s) in series)
        {
            _output.WriteLine($"[{symbol}] running production pipeline once ({s.Bars.Count} bars)...");
            List<(int Bar, double Ambiguity)> trendingBars = TrendingReadyBars(s, symbol);
            _output.WriteLine($"[{symbol}] {trendingBars.Count} Trending Ready bars. Sweeping {cells.Count} cells...");

            int splitBar = (int)(s.Bars.Count * TrainFraction);
            foreach (Cell cell in cells)
            {
                var model = new TimeSeriesMomentumModel(cell.Lookbacks);
                var trainR = new List<double>();
                var oosR = new List<double>();
                foreach ((int bar, double ambiguity) in trendingBars)
                {
                    if (ambiguity >= AmbiguityGate) continue;
                    if (!TryReducedTrade(s, symbol, bar, model, cell, out double r)) continue;
                    if (bar < splitBar - PurgeBars) trainR.Add(r);
                    else if (bar >= splitBar) oosR.Add(r);
                }
                cell.Train[symbol] = Summarize(trainR);
                cell.Oos[symbol] = Summarize(oosR);
            }
        }

        WriteGridCsv(Path.Combine(outputDir, "grid.csv"), cells);
        WriteSummary(Path.Combine(outputDir, "summary.txt"), cells);
        _output.WriteLine($"Done: {cells.Count} cells x {series.Count} symbols. See {outputDir}.");
    }

    /// <summary>One production pipeline pass; returns the (barIndex, ambiguityScore) of every Ready bar
    /// the DecisionEngine arbitrated to Trending.</summary>
    private static List<(int, double)> TrendingReadyBars(HistoricalSeries s, string symbol)
    {
        var spec = Engine.Risk.InstrumentRiskSpecification.FromInstrumentInfo(
            new Core.InstrumentInfo(symbol, Specs[symbol].Tick, 1m, Specs[symbol].PointValue, 2),
            minQuantity: 1, maxQuantity: MaxQuantityCap, quantityStep: 1);
        var policy = new Engine.Risk.RiskPolicy(0.01m, null, null, null, null, null, null, null, 1.5, null);
        var scenario = BacktestScenario.Create(
            s, new BacktestWindow(symbol, s.FirstTimestamp, s.LastTimestamp.AddMinutes(1)), 25_000m, spec, policy);

        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(scenario, WarmupBars, PipelineParameterOverrides.None);

        var bars = new List<(int, double)>();
        foreach (BacktestSignalResult b in result.Bars)
        {
            if (b.Status == BacktestSignalStatus.Ready && b.Decision is { Winner: MarketState.Trending } d)
                bars.Add((b.BarIndex, d.AmbiguityScore));
        }
        return bars;
    }

    /// <summary>Reduced deterministic model of EntryTrigger(Trending) + TradePlan(R-multiple TP) +
    /// intrabar ExecutionSimulator. Returns the trade's realised expectancy in R (grossPriceMove /
    /// stopDistance) - size cancels, so R is size-independent. False = no trade this bar.</summary>
    private static bool TryReducedTrade(
        HistoricalSeries s, string symbol, int bar, TimeSeriesMomentumModel model, Cell cell, out double r)
    {
        r = 0;
        IReadOnlyList<HistoricalBar> b = s.Bars;
        if (bar + 1 + HorizonBars >= b.Count) return false;

        int start = Math.Max(0, bar - HistoryWindow + 1);
        var history = new List<decimal>(bar - start + 1);
        for (int i = start; i <= bar; i++) history.Add(b[i].Close);

        var decision = new DecisionResult { Winner = MarketState.Trending, Confidence = 0.8 };
        var ctx = new ScientificModelContext(
            new MarketContext(b[bar].Timestamp, bar, history, b[bar].Close),
            decision,
            new MethodologySelection(
                decision,
                new QuantitativeMethodology("TrendFollowingMethodology", "", "Time Series Momentum",
                    Array.Empty<string>(), "SPRT", new[] { "Trending" }, "1.0", Array.Empty<string>()),
                b[bar].Timestamp, "1.0", ""),
            Array.Empty<ScientificModelResult>());

        ScientificModelResult mr = model.Evaluate(ctx);
        if (!mr.Success || mr.Metrics is null) return false;
        if (!TryD(mr.Metrics, "MomentumScore", out double score) ||
            !TryD(mr.Metrics, "MomentumConfidence", out double conf) ||
            !TryD(mr.Metrics, "CurrentVolatility", out double vol)) return false;
        if (conf < cell.MinConf || score == 0.0 || !(vol > 0.0)) return false;

        bool isBuy = score > 0.0;
        decimal tick = Specs[symbol].Tick;
        decimal entry = b[bar + 1].Open;
        if (entry <= 0m) return false;

        decimal rawStopDist = (decimal)(cell.StopMult * vol);
        decimal stopDist = Math.Round(rawStopDist / tick, MidpointRounding.AwayFromZero) * tick;
        if (stopDist <= 0m) stopDist = tick;

        decimal stopPrice = isBuy ? entry - stopDist : entry + stopDist;
        decimal tpDist = stopDist * (decimal)cell.TpR;
        decimal tpPrice = isBuy ? entry + tpDist : entry - tpDist;

        // NOTE: no affordability / position-sizing gate here. Expectancy is measured in R
        // (grossPriceMove / stopDistance), which is size-independent, so whether a $250 budget can
        // afford one contract of THIS instrument is a capital-scaling question, orthogonal to whether
        // the parameter cell has an edge. Filtering on it would bias the grid toward tight-stop cells.

        decimal exit = 0m;
        bool resolved = false;
        for (int k = bar + 1; k <= bar + 1 + HorizonBars; k++)
        {
            HistoricalBar mb = b[k];
            bool stopHit = isBuy ? mb.Low <= stopPrice : mb.High >= stopPrice;
            bool tpHit = isBuy ? mb.High >= tpPrice : mb.Low <= tpPrice;
            if (stopHit && tpHit) { exit = stopPrice; resolved = true; break; }     // ambiguous -> conservative
            if (stopHit) { exit = stopPrice; resolved = true; break; }
            if (tpHit) { exit = tpPrice; resolved = true; break; }
        }
        if (!resolved) exit = b[bar + 1 + HorizonBars].Close;

        decimal grossMove = isBuy ? exit - entry : entry - exit;
        r = (double)(grossMove / stopDist);
        return true;
    }

    private static bool TryD(IReadOnlyDictionary<string, object> m, string k, out double v)
    {
        if (m.TryGetValue(k, out object? o) && o is double d && double.IsFinite(d)) { v = d; return true; }
        v = 0; return false;
    }

    private static SliceMetrics Summarize(List<double> rs)
    {
        if (rs.Count == 0) return new SliceMetrics(0, 0, 0);
        int wins = rs.Count(x => x >= 0);
        return new SliceMetrics(rs.Count, wins, rs.Sum());
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] v = values.OrderBy(x => x).ToArray();
        if (v.Length == 0) return double.NaN;
        return v.Length % 2 == 1 ? v[v.Length / 2] : 0.5 * (v[v.Length / 2 - 1] + v[v.Length / 2]);
    }

    private void WriteSummary(string path, List<Cell> cells)
    {
        var ranked = cells.Select(c =>
        {
            double[] trainR = TrainSymbols.Select(sym => c.Train[sym]).Where(m => m.Trades >= MinTradesForSymbol).Select(m => m.ExpectancyR).ToArray();
            double[] oosMarketR = OosSymbols.Select(sym => c.Train[sym]).Where(m => m.Trades >= MinTradesForSymbol).Select(m => m.ExpectancyR).ToArray();
            double[] oosTempR = TrainSymbols.Concat(OosSymbols).Select(sym => c.Oos[sym]).Where(m => m.Trades >= MinTradesForSymbol / 2).Select(m => m.ExpectancyR).ToArray();
            return new
            {
                Cell = c,
                TrainMedianR = Median(trainR),
                OosMarketMedianR = Median(oosMarketR),
                OosTempMedianR = Median(oosTempR),
                TrainSyms = trainR.Length,
                TrainTrades = TrainSymbols.Sum(sym => c.Train[sym].Trades),
            };
        })
        .Where(x => x.TrainSyms >= 2 && !double.IsNaN(x.TrainMedianR))
        .OrderByDescending(x => x.TrainMedianR)
        .ToList();

        var lines = new List<string>
        {
            $"TRENDING CALIBRATION - {DateTime.UtcNow:O}",
            $"Grid: {cells.Count} cells. 6 markets, M5, ~59d each. TRAIN={string.Join(",", TrainSymbols)} OOS={string.Join(",", OosSymbols)}.",
            $"Metric = expectancy per trade in R (grossPriceMove / stopDistance). MinTrades/symbol = {MinTradesForSymbol}.",
            "",
            "PER-MARKET TRADE COUNTS (default cell, train slice):",
        };
        Cell? def = cells.FirstOrDefault(c => c.Id == "default|mc0.15|sl2.5|tp2.5");
        if (def is not null)
        {
            foreach (string sym in TrainSymbols.Concat(OosSymbols))
                lines.Add($"  {sym,-4} train={def.Train[sym].Trades,-5} oosTemporal={def.Oos[sym].Trades}");
        }
        lines.Add("");
        lines.Add("TOP 12 CELLS BY CROSS-MARKET TRAIN MEDIAN EXPECTANCY-R:");
        lines.Add("rank cellId                              trainMedR  oosMktMedR  oosTempMedR  trainSyms trainTrades");
        for (int i = 0; i < Math.Min(12, ranked.Count); i++)
        {
            var x = ranked[i];
            lines.Add($"{i + 1,-4} {x.Cell.Id,-35} {x.TrainMedianR,9:F4} {x.OosMarketMedianR,11:F4} {x.OosTempMedianR,12:F4} {x.TrainSyms,9} {x.TrainTrades,11}");
        }
        lines.Add("");

        if (ranked.Count == 0)
        {
            lines.Add("VERDICT: no cell produced >= 12 trades on >= 2 TRAIN markets. Trending is too sparse to calibrate on this data.");
        }
        else
        {
            var best = ranked[0];
            bool a = best.TrainMedianR > 0, m = best.OosMarketMedianR > 0, t = best.OosTempMedianR > 0;
            lines.Add($"BEST TRAIN CELL: {best.Cell.Id}");
            lines.Add($"  TRAIN median expectancy-R = {best.TrainMedianR:F4}  ({(a ? "positive" : "NOT positive")})");
            lines.Add($"  OOS-market median R       = {best.OosMarketMedianR:F4}  ({(m ? "holds" : "does NOT hold")})");
            lines.Add($"  OOS-temporal median R     = {best.OosTempMedianR:F4}  ({(t ? "holds" : "does NOT hold")})");
            lines.Add("");
            lines.Add(a && m && t
                ? $"VERDICT: PROVISIONAL SELECTION = {best.Cell.Id} (positive on TRAIN and both OOS checks; ~59d data => provisional)."
                : "VERDICT: NO ROBUST CELL. The best TRAIN cell does not generalise. Trending is not profitably calibratable on this data via these 4 knobs - the entry/model design needs revisiting.");
        }

        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        foreach (string l in lines) _output.WriteLine(l);
    }

    private static void WriteGridCsv(string path, List<Cell> cells)
    {
        var sb = new List<string> { "cellId,lookbacks,minConf,stopMult,tpR,symbol,slice,trades,winRate,expectancyR,sumR" };
        foreach (Cell c in cells)
        {
            foreach ((string sym, SliceMetrics mm) in c.Train) sb.Add(Row(c, sym, "train", mm));
            foreach ((string sym, SliceMetrics mm) in c.Oos) sb.Add(Row(c, sym, "oosTemporal", mm));
        }
        File.WriteAllText(path, string.Join("\n", sb) + "\n");

        static string Row(Cell c, string sym, string slice, SliceMetrics m) => string.Join(",",
            c.Id, c.LookbackLabel,
            c.MinConf.ToString("0.00", CultureInfo.InvariantCulture),
            c.StopMult.ToString("0.0", CultureInfo.InvariantCulture),
            c.TpR.ToString("0.0", CultureInfo.InvariantCulture),
            sym, slice, m.Trades,
            m.WinRate.ToString("0.000", CultureInfo.InvariantCulture),
            m.ExpectancyR.ToString("0.0000", CultureInfo.InvariantCulture),
            m.SumR.ToString("0.00", CultureInfo.InvariantCulture));
    }

    private Dictionary<string, HistoricalSeries> LoadAllSeries()
    {
        // Cache the raw downloads to disk so re-running the (harness-side) grid does not re-hit Yahoo
        // six times. The cache is keyed by UTC date, so it self-invalidates daily (the rolling window).
        string cacheDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Research", "TrendingCalibration", "_cache", DateTime.UtcNow.ToString("yyyyMMdd"));
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
                _output.WriteLine($"[{sym}] loaded {result[sym].Bars.Count} bars from cache.");
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
                DateTime.Parse(p[0], CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                decimal.Parse(p[1], CultureInfo.InvariantCulture), decimal.Parse(p[2], CultureInfo.InvariantCulture),
                decimal.Parse(p[3], CultureInfo.InvariantCulture), decimal.Parse(p[4], CultureInfo.InvariantCulture),
                decimal.Parse(p[5], CultureInfo.InvariantCulture)));
        }
        return HistoricalSeries.Create(symbol, "M5", "UTC", "Yahoo(Continuous)-cached", bars);
    }
}
