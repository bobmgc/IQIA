using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Engine.TradePlan;
using Xunit;

namespace IQIAIndicator.Tests.Research.OrderFlowFeasibility;

/// <summary>
/// QDE-019 — READ-ONLY. Terminal test of the order-flow axis: does per-bar FOOTPRINT structure (POC
/// location, absorption/concentration, where net buying/selling sat in the bar, stacked diagonal
/// imbalances, aggression skew) at the MeanReverting signal bar predict the next-H-bar MES return, net of
/// cost? No production type modified. Consumes the CSV from <c>OrderFlowFootprintExport</c>. Methodology
/// identical to QDE-014/017 (tercile segmentation, directional rule, 95% CI, purged train/OOS). Skips
/// until the CSV exists.
/// </summary>
public sealed class FootprintConditioningPilotTests
{
    private const int WarmupBars = 128;
    private const int HorizonBars = 10;
    private const decimal InitialCapital = 25_000m;
    private const decimal PointValue = 5m;
    private const decimal TickSize = 0.25m;
    private const double RoundTripCostPts = 0.77;
    private static readonly int[] Horizons = { 1, 3, 6, 12 };
    private const double TrainFraction = 0.60;
    private const int PurgeBars = 40;

    private readonly ITestOutputHelper _output;
    private readonly List<string> _log = new();
    public FootprintConditioningPilotTests(ITestOutputHelper output) => _output = output;
    private void W(string s) { _output.WriteLine(s); _log.Add(s); }
    private static string Sgn(double v) => (v < 0 ? "-" : "+") + Math.Abs(v).ToString("0.0000", CultureInfo.InvariantCulture);

    private static RiskPolicy Policy() => new(0.01m, null, null, null, null, null, null, null, null, null);
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: TickSize, TickValue: 1.25m, PointValue: PointValue, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private sealed record Tr(int Bar, DateTime Ts, double Entry, Dictionary<string, double> F);

    [Fact]
    public void Footprint_Structure_Conditions_MES_Forward_Return()
    {
        string? path = FootprintCsvBarSource.FindLatest();
        Assert.SkipUnless(path is not null && File.Exists(path),
            "No orderflow_footprint_*.csv - run the 'OrderFlowFootprintExport' ATAS indicator first (QDE-019).");

        IReadOnlyList<FootprintBar> rows = FootprintCsvBarSource.ReadClean(path!);
        W($"CSV: {path}   clean bars={rows.Count}  ({rows[0].Timestamp:yyyy-MM-dd}..{rows[^1].Timestamp:yyyy-MM-dd})");

        // basic footprint sanity across the corpus
        W($"footprint sanity: fpLevels median={Median(rows.Select(r => (double)r.FpLevels)):0}  " +
          $"pocConc median={Median(rows.Select(r => r.PocConc)):0.000}  " +
          $"stackedBuyTop mean={rows.Average(r => (double)r.StackedBuyImbTop):0.00}  " +
          $"stackedSellBot mean={rows.Average(r => (double)r.StackedSellImbBot):0.00}  " +
          $"aggSkew median={Median(rows.Select(r => r.AggSkewTopBot)):+0.000;-0.000}");

        HistoricalSeries series = FootprintCsvBarSource.ToHistoricalSeries("MES", "M5", rows);
        var scenario = BacktestScenario.Create(
            series, new BacktestWindow("QDE019", series.FirstTimestamp, series.LastTimestamp.AddSeconds(1)),
            InitialCapital, Spec(), Policy());
        MeasurementConfiguration meas = MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 });
        ExecutionConfiguration exec = ExecutionConfiguration.Create(HorizonBars);
        PnLConfiguration pnl = PnLConfiguration.Create(
            InstrumentPnLSpecification.Create("MES", priceUnitValue: PointValue, currency: "USD"), 1, InitialCapital);
        BacktestFullResult full = new BacktestEngine().RunFullBacktest(scenario, WarmupBars, meas, exec, pnl);

        double[] close = rows.Select(r => (double)r.Close).ToArray();
        long[] ts = rows.Select(r => r.Timestamp.Ticks).ToArray();

        var trades = new List<Tr>();
        foreach (BacktestSignalResult sb in full.SignalResult.Bars)
        {
            if (sb.Status != BacktestSignalStatus.Ready || sb.Decision is not { Winner: MarketState.MeanReverting }) continue;
            if (sb.TradePlan is not { } plan) continue;
            bool isBuy = plan.Direction == DirectionCandidate.BUY_CANDIDATE;
            bool isSell = plan.Direction == DirectionCandidate.SELL_CANDIDATE;
            if (!isBuy && !isSell) continue;
            int k = sb.BarIndex;
            if (k + 1 + HorizonBars >= rows.Count) continue;
            FootprintBar r = rows[k];

            var f = new Dictionary<string, double>
            {
                // signed features (directional hypotheses)
                ["pocOffTicks"] = r.PocOffTicks,                                  // POC above(+)/below(-) close
                ["pocVsClosePos"] = r.PocPosInRange - r.ClosePosInRange,          // POC higher(+)/lower(-) than close, in-bar
                ["pocPosCentred"] = r.PocPosInRange - 0.5,                        // POC upper(+)/lower(-) half
                ["netStackedImb"] = r.StackedBuyImbTop - r.StackedSellImbBot,     // buy stacks (top) minus sell stacks (bottom)
                ["aggSkewTopBot"] = r.AggSkewTopBot,                             // ask-in-top minus bid-in-bottom
                ["buyVsSellPos"] = r.MaxPosDeltaPosInRange - r.MaxNegDeltaPosInRange, // strongest buying above(+) strongest selling
                ["posDeltaVsClose"] = r.MaxPosDeltaPosInRange - r.ClosePosInRange, // strong buying above(+) the close
                // unsigned features (absorption / structure -> magnitude)
                ["pocConc"] = r.PocConc,
                ["top3Conc"] = r.Top3Conc,
                ["fpLevels"] = r.FpLevels,
                ["totalStacked"] = r.StackedBuyImbTop + r.StackedSellImbBot,
            };
            trades.Add(new Tr(k, sb.Timestamp, (double)rows[k + 1].Open, f));
        }
        trades = trades.OrderBy(t => t.Bar).ToList();
        W($"MeanReverting trades = {trades.Count}");
        if (trades.Count < 300) { W("too few trades - abort"); WriteReport(); return; }

        int splitBar = trades[(int)(trades.Count * TrainFraction)].Bar;
        W($"split at bar {splitBar} (purge {PurgeBars})");

        string[] signed = { "pocOffTicks", "pocVsClosePos", "pocPosCentred", "netStackedImb", "aggSkewTopBot", "buyVsSellPos", "posDeltaVsClose" };
        string[] unsigned = { "pocConc", "top3Conc", "fpLevels", "totalStacked" };

        foreach (int h in Horizons)
        {
            long maxSpan = (long)(5.0 * h * 1.6 * TimeSpan.TicksPerMinute);
            var use = trades.Where(t => t.Bar + h < close.Length && ts[t.Bar + h] - ts[t.Bar] <= maxSpan).ToList();
            // forward move measured from the FILL (bar+1 open) to (bar+1+... ) - keep consistent with QDE-017:
            var fwd = use.Select(t => close[Math.Min(t.Bar + h, close.Length - 1)] - t.Entry).ToArray();

            W("");
            W($"================  HORIZON H={h} bars ({5 * h} min)  —  n={use.Count}  ================");
            double meanFwd = fwd.Average(), meanAbs = fwd.Select(Math.Abs).Average();
            W($"unconditional: mean fwdMove={Sgn(meanFwd)} pt   mean |fwdMove|={meanAbs:0.000} pt   RT cost={RoundTripCostPts} pt");

            // ---- directional rule per signed feature ----
            W("  -- directional rule 'go with sign(feature)', exit at H, net of cost --");
            W("  feature            n     hitRate  gross pt (CI)      NET pt (CI)        verdict            TRAIN/OOS net");
            foreach (string name in signed)
            {
                var bets = new List<(double p, int bar)>();
                int hit = 0;
                for (int i = 0; i < use.Count; i++)
                {
                    double s = Math.Sign(use[i].F[name]);
                    if (s == 0) continue;
                    double signed01 = s * fwd[i];
                    bets.Add((signed01, use[i].Bar));
                    if (signed01 > 0) hit++;
                }
                if (bets.Count < 50) { W($"  {name,-16} n={bets.Count} (<50)"); continue; }
                (double gm, double gci) = MeanCi(bets.Select(b => b.p));
                (double nm, double nci) = MeanCi(bets.Select(b => b.p - RoundTripCostPts));
                (double trm, double _) = MeanCi(bets.Where(b => b.bar < splitBar - PurgeBars).Select(b => b.p - RoundTripCostPts));
                (double oom, double _2) = MeanCi(bets.Where(b => b.bar >= splitBar).Select(b => b.p - RoundTripCostPts));
                W($"  {name,-16} {bets.Count,6}  {hit / (double)bets.Count,7:0.000}  {Sgn(gm)} +/-{gci:0.000}  {Sgn(nm)} +/-{nci:0.000}  " +
                  $"{Verdict(nm, nci),-18} {Sgn(trm)} / {Sgn(oom)}");
            }

            // ---- terciles: signed features vs signed fwdMove ; unsigned vs |fwdMove| ----
            W("  -- tercile mean of target (signed feats -> signed fwdMove ; unsigned -> |fwdMove|) --");
            foreach (string name in signed) Terciles(name, use, fwd, splitBar, signedTarget: true);
            foreach (string name in unsigned) Terciles(name, use, fwd, splitBar, signedTarget: false);

            // ---- Pearson ----
            double band = 1.96 / Math.Sqrt(use.Count);
            W($"  -- Pearson(feature, target)  [band +/-{band:0.0000}] --");
            foreach (string name in signed)
                W($"     {name,-16} r={Sgn(Pearson(use.Select(t => t.F[name]).ToArray(), fwd))}   (vs signed fwdMove)");
            foreach (string name in unsigned)
                W($"     {name,-16} r={Sgn(Pearson(use.Select(t => t.F[name]).ToArray(), fwd.Select(Math.Abs).ToArray()))}   (vs |fwdMove|)");
        }

        WriteReport();
        Assert.True(trades.Count > 300);
    }

    private void Terciles(string name, List<Tr> use, double[] fwd, int splitBar, bool signedTarget)
    {
        var p = new (double f, double y, int bar)[use.Count];
        for (int i = 0; i < use.Count; i++)
            p[i] = (use[i].F[name], signedTarget ? fwd[i] : Math.Abs(fwd[i]), use[i].Bar);
        var s = p.OrderBy(x => x.f).ToArray();
        double lo = s[s.Length / 3].f, hi = s[2 * s.Length / 3].f;
        var res = new List<string>();
        foreach ((string lbl, Func<double, bool> sel) in new (string, Func<double, bool>)[]
        { ("lo", f => f <= lo), ("mid", f => f > lo && f <= hi), ("hi", f => f > hi) })
        {
            var seg = p.Where(x => sel(x.f)).ToArray();
            if (seg.Length == 0) { res.Add($"{lbl}:n0"); continue; }
            (double m, double ci) = MeanCi(seg.Select(x => x.y));
            (double om, double _) = MeanCi(seg.Where(x => x.bar >= splitBar).Select(x => x.y));
            res.Add($"{lbl} {Sgn(m)}+/-{ci:0.000}(OOS {Sgn(om)})");
        }
        W($"     {name,-16} [{(signedTarget ? "signed" : "abs")}]  {string.Join("   ", res)}");
    }

    private static string Verdict(double m, double ci) => m - ci > 0 ? "NET+ ROBUST" : m + ci < 0 ? "net- robust" : "straddles 0";

    private static double Median(IEnumerable<double> xs)
    {
        double[] v = xs.OrderBy(x => x).ToArray(); if (v.Length == 0) return double.NaN;
        return v.Length % 2 == 1 ? v[v.Length / 2] : 0.5 * (v[v.Length / 2 - 1] + v[v.Length / 2]);
    }
    private static (double mean, double ci95) MeanCi(IEnumerable<double> xs)
    {
        double[] v = xs.ToArray(); if (v.Length == 0) return (double.NaN, double.NaN);
        double m = v.Average(); if (v.Length < 2) return (m, double.NaN);
        double sd = Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / (v.Length - 1));
        return (m, 1.96 * sd / Math.Sqrt(v.Length));
    }
    private static double Pearson(double[] a, double[] b)
    {
        int n = Math.Min(a.Length, b.Length); if (n < 2) return 0;
        double ma = a.Take(n).Average(), mb = b.Take(n).Average(), sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < n; i++) { double da = a[i] - ma, db = b[i] - mb; sab += da * db; saa += da * da; sbb += db * db; }
        double d = Math.Sqrt(saa * sbb); return d <= 1e-15 ? 0 : sab / d;
    }
    private void WriteReport()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) return;
        string od = Path.Combine(dir, "Research", "OrderFlowFeasibility", "Output");
        Directory.CreateDirectory(od);
        File.WriteAllText(Path.Combine(od, "footprint_conditioning_pilot.txt"), string.Join("\n", _log) + "\n");
    }
}
