using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.TradePlan;
using IQIAIndicator.Tests.BacktestTests.Yahoo;
using Xunit;

namespace IQIAIndicator.Tests.Research.StopLossContractAudit;

/// <summary>
/// READ-ONLY empirical measurement for the 2026-08-30 Stop Loss / Take Profit / Position Sizing
/// contract audit. Modifies no production type. Runs the unmodified
/// <see cref="BacktestEngine.RunSignalPipeline"/> over the canonical Yahoo MES M5 window and dumps the
/// realised distribution of the production stop distance, the R:R it produces and the position size a
/// canonical risk budget would resolve to.
/// </summary>
public sealed class StopLossContractAuditTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public StopLossContractAuditTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private const decimal TickSize = 0.25m;
    private const decimal PointValue = 5m;      // MES: $5 per index point
    private const decimal InitialCapital = 25_000m;

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: TickSize, TickValue: 1.25m, PointValue: PointValue, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private sealed record SignalRow(
        int Bar, string Regime, string Direction, decimal Entry, decimal Stop, decimal? Target,
        decimal StopPts, decimal StopTicks, double? CurrentVol, string VolRegime,
        double? Rr, decimal RiskPerUnit1pct, double RawContracts1pct, decimal RiskPerUnit2pct, double RawContracts2pct);

    [Fact]
    public void Measure_MES_M5_StopLoss_TakeProfit_PositionSizing()
    {
        HistoricalSeries series;
        try
        {
            series = _yahoo.Require();
        }
        catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {ex.GetType().Name}: {ex.Message}");
            return;
        }

        const int warmupBars = 128;
        var scenario = BacktestScenario.Create(
            series, new BacktestWindow("SL-CONTRACT-AUDIT", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
            InitialCapital, Spec(), new RiskPolicy(0.01m, null, null, null, null, null, null, null, null, null));

        BacktestSignalPipelineResult result =
            new BacktestEngine().RunSignalPipeline(scenario, warmupBars, PipelineParameterOverrides.None);

        var rows = new List<SignalRow>();
        int directionalBars = 0, directionalWithStop = 0;

        foreach (BacktestSignalResult bar in result.Bars)
        {
            if (bar.Status != BacktestSignalStatus.Ready) continue;
            if (bar.Decision is not { } decision) continue;
            if (bar.TradePlan is not { } plan) continue;
            bool isBuy = plan.Direction == global::IQIAIndicator.Engine.EntryTrigger.DirectionCandidate.BUY_CANDIDATE;
            bool isSell = plan.Direction == global::IQIAIndicator.Engine.EntryTrigger.DirectionCandidate.SELL_CANDIDATE;
            if (!isBuy && !isSell) continue;
            directionalBars++;

            if (plan.EntryPrice is not decimal entry || plan.StopLoss is not decimal stop) continue;
            directionalWithStop++;

            decimal stopPts = Math.Abs(entry - stop);
            if (stopPts <= 0m) continue;

            (double? currentVol, string volRegime) = ReadVolatility(bar.EntryTrigger);

            decimal rpu1 = stopPts * PointValue;
            double raw1 = (double)(InitialCapital * 0.01m / rpu1);
            decimal rpu2 = stopPts * PointValue;
            double raw2 = (double)(InitialCapital * 0.02m / rpu2);

            rows.Add(new SignalRow(
                bar.BarIndex, decision.Winner.ToString(), isBuy ? "BUY" : "SELL", entry, stop, plan.TakeProfit,
                stopPts, stopPts / TickSize, currentVol, volRegime,
                plan.RiskRewardRatio, rpu1, raw1, rpu2, raw2));
        }

        _output.WriteLine("=== DATASET ===");
        _output.WriteLine($"Symbol={series.Symbol} TF={series.TimeFrame} Bars={series.Count} " +
            $"Range={series.FirstTimestamp:O}..{series.LastTimestamp:O}");
        _output.WriteLine($"ReadyBars={result.ReadyBars} DirectionalBars={directionalBars} " +
            $"DirectionalWithStop={directionalWithStop} Measured={rows.Count}");
        _output.WriteLine($"BuyCount={result.BuyCount} SellCount={result.SellCount} " +
            $"TradePlanReady={result.TradePlanReadyCount} TradePlanSignalOnly={result.TradePlanSignalOnlyCount}");

        if (rows.Count == 0)
        {
            _output.WriteLine("No directional stop was produced on this window - nothing to summarise.");
            return;
        }

        DumpDistribution("ALL regimes", rows);
        foreach (string reg in rows.Select(r => r.Regime).Distinct().OrderBy(x => x))
            DumpDistribution(reg, rows.Where(r => r.Regime == reg).ToList());

        // Position sizing: fraction that cannot afford a single contract before flooring.
        int sub1_1pct = rows.Count(r => r.RawContracts1pct < 1.0);
        int sub1_2pct = rows.Count(r => r.RawContracts2pct < 1.0);
        _output.WriteLine("");
        _output.WriteLine("=== POSITION SIZING (raw contracts BEFORE floor/step) ===");
        _output.WriteLine($"budget = InitialCapital x MaxRiskPerTradePercent ; riskPerUnit = stopPts x PointValue({PointValue})");
        _output.WriteLine($"1% budget (${InitialCapital * 0.01m:0}) : rawContracts<1 on {sub1_1pct}/{rows.Count} = {Pct(sub1_1pct, rows.Count):0.0}%  " +
            $"(min={rows.Min(r => r.RawContracts1pct):0.00} median={Median(rows.Select(r => r.RawContracts1pct)):0.00} max={rows.Max(r => r.RawContracts1pct):0.00})");
        _output.WriteLine($"2% budget (${InitialCapital * 0.02m:0}) : rawContracts<1 on {sub1_2pct}/{rows.Count} = {Pct(sub1_2pct, rows.Count):0.0}%  " +
            $"(min={rows.Min(r => r.RawContracts2pct):0.00} median={Median(rows.Select(r => r.RawContracts2pct)):0.00} max={rows.Max(r => r.RawContracts2pct):0.00})");

        // "Abnormally large" defined empirically as > p90 of the stop distance.
        decimal p90 = Percentile(rows.Select(r => r.StopPts), 0.90);
        var large = rows.Where(r => r.StopPts > p90).ToList();
        _output.WriteLine("");
        _output.WriteLine("=== EXTREME STOPS ( > p90 of stopPts ) ===");
        _output.WriteLine($"p90 stopPts={p90:0.00} ({p90 / TickSize:0.0} ticks). Count above = {large.Count}/{rows.Count} = {Pct(large.Count, rows.Count):0.0}%");
        _output.WriteLine($"Of those, VolRegime=HIGH : {large.Count(r => r.VolRegime == "HIGH")}/{large.Count}; " +
            $"MEDIUM : {large.Count(r => r.VolRegime == "MEDIUM")}; LOW : {large.Count(r => r.VolRegime == "LOW")}; " +
            $"n/a : {large.Count(r => r.VolRegime is "" or "n/a")}");
        _output.WriteLine($"Baseline VolRegime mix (all rows) : HIGH {rows.Count(r => r.VolRegime == "HIGH")}, " +
            $"MEDIUM {rows.Count(r => r.VolRegime == "MEDIUM")}, LOW {rows.Count(r => r.VolRegime == "LOW")}, " +
            $"n/a {rows.Count(r => r.VolRegime is "" or "n/a")}");

        // stopPts vs currentVolatility correlation (should be ~1.0: stopPts = round(2.0 x vol)).
        var withVol = rows.Where(r => r.CurrentVol is > 0).ToList();
        if (withVol.Count >= 2)
        {
            double corr = Pearson(
                withVol.Select(r => (double)r.StopPts).ToList(),
                withVol.Select(r => r.CurrentVol!.Value).ToList());
            double meanRatio = withVol.Average(r => (double)r.StopPts / r.CurrentVol!.Value);
            _output.WriteLine("");
            _output.WriteLine($"=== stopPts vs CurrentVolatility ({withVol.Count} rows) ===");
            _output.WriteLine($"Pearson(stopPts, currentVol) = {corr:0.0000} ; mean(stopPts/currentVol) = {meanRatio:0.0000} (expected ~2.0)");
        }

        // R:R distribution.
        var rr = rows.Where(r => r.Rr is not null).Select(r => r.Rr!.Value).OrderBy(x => x).ToList();
        if (rr.Count > 0)
        {
            _output.WriteLine("");
            _output.WriteLine($"=== R:R (TakeProfit reachable) : {rr.Count}/{rows.Count} rows have a computable R:R ===");
            _output.WriteLine($"min={rr.First():0.000} p10={Percentile(rr.Select(x => (decimal)x), 0.10):0.000} " +
                $"median={Median(rr.Select(x => (decimal)x)):0.000} p90={Percentile(rr.Select(x => (decimal)x), 0.90):0.000} max={rr.Last():0.000}");
            _output.WriteLine($"R:R < 1.0 on {rr.Count(x => x < 1.0)}/{rr.Count} ; R:R < 1.5 on {rr.Count(x => x < 1.5)}/{rr.Count}");
        }

        WriteCsv(series, rows);
    }

    private void DumpDistribution(string label, IReadOnlyList<SignalRow> rows)
    {
        if (rows.Count == 0) { _output.WriteLine($"[{label}] no rows"); return; }
        var pts = rows.Select(r => r.StopPts).ToList();
        var ticks = rows.Select(r => r.StopTicks).ToList();
        _output.WriteLine("");
        _output.WriteLine($"=== STOP DISTANCE  [{label}]  n={rows.Count} ===");
        _output.WriteLine($"points  : min={pts.Min():0.00} p50={Median(pts):0.00} p90={Percentile(pts, 0.90):0.00} " +
            $"p99={Percentile(pts, 0.99):0.00} max={pts.Max():0.00} mean={pts.Average():0.00}");
        _output.WriteLine($"ticks   : min={ticks.Min():0.0} p50={Median(ticks):0.0} p90={Percentile(ticks, 0.90):0.0} " +
            $"p99={Percentile(ticks, 0.99):0.0} max={ticks.Max():0.0} mean={ticks.Average():0.0}");
        _output.WriteLine($"$ risk/contract (x{PointValue}) : min={pts.Min() * PointValue:0.0} p50={Median(pts) * PointValue:0.0} " +
            $"p90={Percentile(pts, 0.90) * PointValue:0.0} p99={Percentile(pts, 0.99) * PointValue:0.0} max={pts.Max() * PointValue:0.0}");
    }

    private static (double? vol, string regime) ReadVolatility(EntryTriggerCandidate? etc)
    {
        IReadOnlyList<ScientificModelResult>? results =
            etc?.EntryCandidate?.Assessment?.ScientificAssessment?.ScientificResults;
        if (results is null) return (null, "n/a");

        foreach (ScientificModelResult r in results)
        {
            bool isVolSource =
                string.Equals(r.ModelName, "VolatilityModel", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.ModelName, "TimeSeriesMomentumModel", StringComparison.OrdinalIgnoreCase);
            if (!isVolSource || !r.Success || r.Metrics is null) continue;

            double? vol = r.Metrics.TryGetValue(ScientificMetricKeys.CurrentVolatility, out object? rawVol) && rawVol is double dv && double.IsFinite(dv)
                ? dv
                : null;
            string regime = r.Metrics.TryGetValue("VolatilityRegime", out object? rawReg) && rawReg is string s
                ? s
                : "n/a";
            return (vol, regime);
        }
        return (null, "n/a");
    }

    private void WriteCsv(HistoricalSeries series, IReadOnlyList<SignalRow> rows)
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) return;
        string outDir = Path.Combine(dir, "Research", "StopLossContractAudit", "Output");
        Directory.CreateDirectory(outDir);

        var sb = new StringBuilder();
        sb.AppendLine("bar,regime,direction,entry,stop,target,stopPts,stopTicks,currentVol,volRegime,rr,rawContracts1pct,rawContracts2pct");
        foreach (SignalRow r in rows)
            sb.AppendLine(string.Join(",",
                r.Bar, r.Regime, r.Direction,
                r.Entry.ToString(CultureInfo.InvariantCulture), r.Stop.ToString(CultureInfo.InvariantCulture),
                r.Target?.ToString(CultureInfo.InvariantCulture) ?? "",
                r.StopPts.ToString(CultureInfo.InvariantCulture), r.StopTicks.ToString(CultureInfo.InvariantCulture),
                r.CurrentVol?.ToString("G17", CultureInfo.InvariantCulture) ?? "",
                r.VolRegime,
                r.Rr?.ToString("G17", CultureInfo.InvariantCulture) ?? "",
                r.RawContracts1pct.ToString("G17", CultureInfo.InvariantCulture),
                r.RawContracts2pct.ToString("G17", CultureInfo.InvariantCulture)));
        File.WriteAllText(Path.Combine(outDir, "signals.csv"), sb.ToString());
        _output.WriteLine("");
        _output.WriteLine($"Wrote {rows.Count} rows to {Path.Combine(outDir, "signals.csv")}");
    }

    private static double Pct(int c, int t) => t > 0 ? 100.0 * c / t : 0.0;

    private static decimal Median(IEnumerable<decimal> xs) => Percentile(xs, 0.50);
    private static double Median(IEnumerable<double> xs)
    {
        double[] v = xs.OrderBy(x => x).ToArray();
        if (v.Length == 0) return double.NaN;
        return v.Length % 2 == 1 ? v[v.Length / 2] : 0.5 * (v[v.Length / 2 - 1] + v[v.Length / 2]);
    }

    private static decimal Percentile(IEnumerable<decimal> xs, double p)
    {
        decimal[] v = xs.OrderBy(x => x).ToArray();
        if (v.Length == 0) return 0m;
        if (v.Length == 1) return v[0];
        double rank = p * (v.Length - 1);
        int lo = (int)Math.Floor(rank);
        int hi = (int)Math.Ceiling(rank);
        decimal frac = (decimal)(rank - lo);
        return v[lo] + (v[hi] - v[lo]) * frac;
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
}
