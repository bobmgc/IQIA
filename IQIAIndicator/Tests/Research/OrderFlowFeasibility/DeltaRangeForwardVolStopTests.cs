using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.TradePlan;
using Xunit;

namespace IQIAIndicator.Tests.Research.OrderFlowFeasibility;

/// <summary>
/// QDE-018 — READ-ONLY. Does the QDE-017 finding (delta-imbalance inversely predicts forward volatility)
/// carry information ABOUT forward realized volatility BEYOND the production backward sigma
/// (VolatilityModel.CurrentVolatility, 20 trailing returns)? And if so, does a deltaRange-adjusted stop
/// reduce the OUTCOME VARIANCE of the (edge-less) MeanReverting book without worsening its expectancy,
/// vs the production 2*sigma stop? No production type modified; no Risk Engine / VolatilityStopLossModel
/// change. Runs the unchanged RunFullBacktest on the ATAS order-flow M5 corpus, then re-simulates the
/// SL/TP race under baseline vs alt stop sizing on an OOS split.
/// </summary>
public sealed class DeltaRangeForwardVolStopTests
{
    private const int WarmupBars = 128;
    private const int HorizonBars = 10;
    private const decimal InitialCapital = 25_000m;
    private const decimal PointValue = 5m;
    private const decimal TickSize = 0.25m;
    private const double BaseCostRoundTripUsd = 3.855;
    private const double TrainFraction = 0.60;
    private const int PurgeBars = 40;

    private readonly ITestOutputHelper _output;
    private readonly List<string> _log = new();
    public DeltaRangeForwardVolStopTests(ITestOutputHelper output) => _output = output;
    private void W(string s) { _output.WriteLine(s); _log.Add(s); }

    private static RiskPolicy Policy() => new(0.01m, null, null, null, null, null, null, null, null, null);
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: TickSize, TickValue: 1.25m, PointValue: PointValue, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private sealed record T(
        int Bar, DateTime Ts, bool IsBuy, double Entry, double RefPrice,
        double SigmaBack, double DeltaRange, double AbsDeltaRatio, double Mae, double FwdAbs);

    [Fact]
    public void DeltaRange_Forward_Vol_And_Stop_Sizing()
    {
        string? path = OrderFlowCsvBarSource.FindLatest();
        Assert.SkipUnless(path is not null && File.Exists(path),
            "No orderflow_export_*.csv - run the 'OrderFlowExport' ATAS indicator first (QDE-017/018).");

        IReadOnlyList<OrderFlowBar> rows = OrderFlowCsvBarSource.ReadClean(path!);
        W($"CSV: {path}   clean bars={rows.Count}  ({rows[0].Timestamp:yyyy-MM-dd}..{rows[^1].Timestamp:yyyy-MM-dd})");

        HistoricalSeries series = OrderFlowCsvBarSource.ToHistoricalSeries("MES", "M5", rows);
        var scenario = BacktestScenario.Create(
            series, new BacktestWindow("QDE018", series.FirstTimestamp, series.LastTimestamp.AddSeconds(1)),
            InitialCapital, Spec(), Policy());
        MeasurementConfiguration meas = MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 });
        ExecutionConfiguration exec = ExecutionConfiguration.Create(HorizonBars);
        PnLConfiguration pnl = PnLConfiguration.Create(
            InstrumentPnLSpecification.Create("MES", priceUnitValue: PointValue, currency: "USD"), 1, InitialCapital);

        BacktestFullResult full = new BacktestEngine().RunFullBacktest(scenario, WarmupBars, meas, exec, pnl);
        var byBar = new Dictionary<int, BacktestSignalResult>();
        foreach (BacktestSignalResult b in full.SignalResult.Bars) byBar[b.BarIndex] = b;

        double[] close = rows.Select(r => (double)r.Close).ToArray();
        double[] high = rows.Select(r => (double)r.High).ToArray();
        double[] low = rows.Select(r => (double)r.Low).ToArray();

        // ── build the MeanReverting trade table with per-signal-bar features ─────────────────────
        var ts = new List<T>();
        foreach (BacktestSignalResult sb in full.SignalResult.Bars)
        {
            if (sb.Status != BacktestSignalStatus.Ready || sb.Decision is not { Winner: MarketState.MeanReverting }) continue;
            if (sb.TradePlan is not { } plan) continue;
            bool isBuy = plan.Direction == DirectionCandidate.BUY_CANDIDATE;
            bool isSell = plan.Direction == DirectionCandidate.SELL_CANDIDATE;
            if (!isBuy && !isSell) continue;
            if (plan.EntryPrice is not decimal rp) continue;
            int k = sb.BarIndex;
            if (k + 1 + HorizonBars >= rows.Count) continue;

            double sigmaBack = ReadCurrentVolatility(sb.EntryTrigger);
            if (!(sigmaBack > 0)) continue;

            double vol = (double)rows[k].Volume;
            if (vol <= 0) continue;
            double deltaRange = (double)(rows[k].MaxDelta - rows[k].MinDelta) / vol;
            double absDeltaRatio = Math.Abs((double)rows[k].Delta) / vol;

            double entry = close[k + 1];   // Lot 14.10 fill = next bar open ~ we use next close as ref; keep next OPEN:
            entry = (double)rows[k + 1].Open;

            // realized adverse excursion (MAE) and terminal abs move over the holding window, from the fill
            double mae = 0, term;
            for (int j = k + 1; j <= k + 1 + HorizonBars; j++)
            {
                double adverse = isBuy ? entry - low[j] : high[j] - entry;
                if (adverse > mae) mae = adverse;
            }
            term = Math.Abs(close[k + 1 + HorizonBars] - entry);

            ts.Add(new T(k, sb.Timestamp, isBuy, entry, (double)rp, sigmaBack, deltaRange, absDeltaRatio, mae, term));
        }
        ts = ts.OrderBy(x => x.Bar).ToList();
        W($"MeanReverting trades (full horizon available) = {ts.Count}");
        if (ts.Count < 300) { W("too few trades - abort"); WriteReport(); return; }

        int split = ts[(int)(ts.Count * TrainFraction)].Bar;
        var train = ts.Where(x => x.Bar < split - PurgeBars).ToList();
        var oos = ts.Where(x => x.Bar >= split).ToList();
        W($"split at bar {split} (purge {PurgeBars}) -> TRAIN n={train.Count}  OOS n={oos.Count}");

        // ═══ PHASE 0 : incremental forward-vol information of deltaRange over backward sigma ═══════
        W("");
        W("================ PHASE 0 — does deltaRange add forward-vol info beyond backward sigma? ================");
        W("target = MAE (max adverse excursion over the 10-bar hold, points).  log-log OLS, fitted on TRAIN.");
        foreach ((string lbl, List<T> set) in new[] { ("TRAIN", train), ("OOS  ", oos) })
        {
            double[] y = set.Select(x => Math.Log(Math.Max(x.Mae, 1e-6))).ToArray();
            double[] s = set.Select(x => Math.Log(x.SigmaBack)).ToArray();
            double[] d = set.Select(x => Math.Log(Math.Max(x.DeltaRange, 1e-6))).ToArray();

            // fit on TRAIN, score on `set`
            (double a0, double a1) = Ols1(train.Select(x => Math.Log(x.SigmaBack)).ToArray(),
                                          train.Select(x => Math.Log(Math.Max(x.Mae, 1e-6))).ToArray());
            (double b0, double b1, double b2) = Ols2(
                train.Select(x => Math.Log(x.SigmaBack)).ToArray(),
                train.Select(x => Math.Log(Math.Max(x.DeltaRange, 1e-6))).ToArray(),
                train.Select(x => Math.Log(Math.Max(x.Mae, 1e-6))).ToArray());

            double[] predA = set.Select(x => a0 + a1 * Math.Log(x.SigmaBack)).ToArray();
            double[] predB = set.Select(x => b0 + b1 * Math.Log(x.SigmaBack) + b2 * Math.Log(Math.Max(x.DeltaRange, 1e-6))).ToArray();
            double R2a = R2(y, predA), R2b = R2(y, predB);
            double corrSig = Pearson(y, s), corrDelta = Pearson(y, d);
            double partial = PartialCorr(y, d, s);

            W($"  [{lbl}] n={set.Count}   corr(logMAE,logSigma)={corrSig:+0.000}   corr(logMAE,logDeltaRange)={corrDelta:+0.000}");
            W($"         R2(sigma only)={R2a:0.0000}   R2(sigma+deltaRange)={R2b:0.0000}   incremental={R2b - R2a:+0.0000}");
            W($"         partial corr(logMAE, logDeltaRange | logSigma) = {partial:+0.000}   (fitted coeff b2={b2:+0.000})");
        }

        // gate: require OOS incremental R2 >= 0.01 AND OOS partial corr magnitude >= 0.05 to proceed
        (double g0, double g1) = Ols1(train.Select(x => Math.Log(x.SigmaBack)).ToArray(),
                                      train.Select(x => Math.Log(Math.Max(x.Mae, 1e-6))).ToArray());
        (double h0, double h1, double h2) = Ols2(
            train.Select(x => Math.Log(x.SigmaBack)).ToArray(),
            train.Select(x => Math.Log(Math.Max(x.DeltaRange, 1e-6))).ToArray(),
            train.Select(x => Math.Log(Math.Max(x.Mae, 1e-6))).ToArray());
        double[] yo = oos.Select(x => Math.Log(Math.Max(x.Mae, 1e-6))).ToArray();
        double incR2 = R2(yo, oos.Select(x => h0 + h1 * Math.Log(x.SigmaBack) + h2 * Math.Log(Math.Max(x.DeltaRange, 1e-6))).ToArray())
                     - R2(yo, oos.Select(x => g0 + g1 * Math.Log(x.SigmaBack)).ToArray());
        double partOos = PartialCorr(yo,
            oos.Select(x => Math.Log(Math.Max(x.DeltaRange, 1e-6))).ToArray(),
            oos.Select(x => Math.Log(x.SigmaBack)).ToArray());

        W("");
        if (incR2 < 0.01 || Math.Abs(partOos) < 0.05)
        {
            W($"=> PHASE 0 GATE FAILED: OOS incremental R2 = {incR2:+0.0000} (need >= 0.01), " +
              $"OOS partial corr = {partOos:+0.000} (need |.| >= 0.05).");
            W("=> deltaRange does NOT add forward-vol information beyond the production backward sigma.");
            W("=> Option 2 (deltaRange-informed stop sizing) has NO empirical basis. Phase 1 not run.");
            WriteReport();
            Assert.True(ts.Count > 300);
            return;
        }
        W($"=> PHASE 0 PASSED: OOS incremental R2 = {incR2:+0.0000}, OOS partial corr = {partOos:+0.000}. Running Phase 1.");

        // ═══ PHASE 1 : baseline 2*sigma stop  vs  deltaRange-adjusted stop, on OOS ═══════════════
        W("");
        W("================ PHASE 1 — baseline 2*sigma stop  vs  deltaRange-adjusted stop (OOS) ================");
        // alt stop scale calibrated on TRAIN: altStop = baseline * (medianDeltaRange_TRAIN / deltaRange)^p
        double medDR = Median(train.Select(x => x.DeltaRange));
        double bestP = 0; double bestObj = double.MaxValue;
        foreach (double p in Range(0.0, 1.0, 0.05))
        {
            var ratios = train.Select(x =>
            {
                double baseStop = 2.0 * x.SigmaBack;
                double alt = baseStop * Math.Pow(medDR / Math.Max(x.DeltaRange, 1e-6), p);
                return x.Mae / Math.Max(alt, 1e-6);          // want this ~constant across trades
            }).ToArray();
            double cv = StdDev(ratios) / Math.Max(1e-9, ratios.Average());   // coefficient of variation
            if (cv < bestObj) { bestObj = cv; bestP = p; }
        }
        W($"  calibrated on TRAIN: p={bestP:0.00}  (minimises CV of MAE/altStop; medianDeltaRange_TRAIN={medDR:0.0000})");

        double SimR(T x, Func<T, double> stopDist, out string exit)
        {
            var r = rows;
            int k = x.Bar;
            double sd = Math.Round(stopDist(x) / (double)TickSize, MidpointRounding.AwayFromZero) * (double)TickSize;
            if (sd <= 0) sd = (double)TickSize;
            double slPx = x.IsBuy ? x.Entry - sd : x.Entry + sd;
            // MeanReverting target = Kalman equilibrium (plan ref price side) -> use refPrice as the equilibrium proxy
            // (the pipeline's TP). Distance from the SIGNAL ref price, re-anchored to the fill (Lot 15.5).
            double tpDist = Math.Abs(x.RefPrice - x.Entry);   // deviation-to-equilibrium at the signal
            double tpPx = x.IsBuy ? x.Entry + tpDist : x.Entry - tpDist;
            for (int j = k + 1; j <= k + 1 + HorizonBars; j++)
            {
                double hi = (double)r[j].High, lo = (double)r[j].Low;
                bool stop = x.IsBuy ? lo <= slPx : hi >= slPx;
                bool tgt = tpDist > 0 && (x.IsBuy ? hi >= tpPx : lo <= tpPx);
                if (stop && tgt) { exit = "Ambiguous"; return x.IsBuy ? slPx - x.Entry : x.Entry - slPx; }
                if (stop) { exit = "StopLoss"; return x.IsBuy ? slPx - x.Entry : x.Entry - slPx; }
                if (tgt) { exit = "TakeProfit"; return x.IsBuy ? tpPx - x.Entry : x.Entry - tpPx; }
            }
            exit = "TimeHorizon";
            double px = (double)r[k + 1 + HorizonBars].Close;
            return x.IsBuy ? px - x.Entry : x.Entry - px;
        }

        void Block(string label, Func<T, double> stopDist)
        {
            var pnlR = new List<double>();
            var pnlUsdNet = new List<double>();
            var risk = new List<double>();
            var exits = new Dictionary<string, int>();
            foreach (T x in oos)
            {
                double sd = stopDist(x);
                double movePts = SimR(x, stopDist, out string ex);
                double riskUsd = sd * (double)PointValue;
                double grossUsd = movePts * (double)PointValue;
                double netUsd = grossUsd - BaseCostRoundTripUsd;
                pnlR.Add(netUsd / Math.Max(1e-9, riskUsd));
                pnlUsdNet.Add(netUsd);
                risk.Add(riskUsd);
                exits[ex] = exits.GetValueOrDefault(ex) + 1;
            }
            (double m, double ci) = MeanCi(pnlR);
            double sd2 = StdDev(pnlR);
            double worst = pnlR.Min();
            double dd = MaxDrawdown(pnlUsdNet);
            W($"  [{label}] n={oos.Count}");
            W($"     stop $ risk/trade: median={Median(risk):0.0}  mean={risk.Average():0.0}");
            W($"     exits: {string.Join(", ", exits.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {100.0 * k.Value / oos.Count:0}%"))}");
            W($"     expectancy NET: {m:+0.000} +/- {ci:0.000} R   ({pnlUsdNet.Average():+0.00} $/trade)");
            W($"     std(per-trade R) = {sd2:0.000}   worst R = {worst:0.00}   max $ drawdown = {dd:0.0}");
        }

        Block("BASELINE 2*sigma        ", x => 2.0 * x.SigmaBack);
        Block("ALT deltaRange-adjusted ", x => 2.0 * x.SigmaBack * Math.Pow(medDR / Math.Max(x.DeltaRange, 1e-6), bestP));

        W("");
        W("Verdict criterion: ALT reduces std(per-trade R) and/or max drawdown on OOS WITHOUT a robustly");
        W("worse net expectancy (its CI overlaps BASELINE's). The book has no edge; the target is variance, not profit.");

        WriteReport();
        Assert.True(ts.Count > 300);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────
    private static double ReadCurrentVolatility(EntryTriggerCandidate? etc)
    {
        IReadOnlyList<ScientificModelResult>? results = etc?.EntryCandidate?.Assessment?.ScientificAssessment?.ScientificResults;
        if (results is null) return double.NaN;
        foreach (ScientificModelResult r in results)
        {
            if (!string.Equals(r.ModelName, "VolatilityModel", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(r.ModelName, "TimeSeriesMomentumModel", StringComparison.OrdinalIgnoreCase)) continue;
            if (!r.Success || r.Metrics is null) continue;
            if (r.Metrics.TryGetValue(ScientificMetricKeys.CurrentVolatility, out object? raw) && raw is double d && double.IsFinite(d) && d > 0)
                return d;
        }
        return double.NaN;
    }

    private static IEnumerable<double> Range(double a, double b, double step)
    { for (double v = a; v <= b + 1e-9; v += step) yield return v; }

    private static (double a0, double a1) Ols1(double[] x, double[] y)
    {
        int n = x.Length; double mx = x.Average(), my = y.Average();
        double sxx = 0, sxy = 0;
        for (int i = 0; i < n; i++) { sxx += (x[i] - mx) * (x[i] - mx); sxy += (x[i] - mx) * (y[i] - my); }
        double a1 = sxx <= 1e-15 ? 0 : sxy / sxx;
        return (my - a1 * mx, a1);
    }

    private static (double b0, double b1, double b2) Ols2(double[] x1, double[] x2, double[] y)
    {
        int n = y.Length;
        double s11 = 0, s12 = 0, s22 = 0, s1y = 0, s2y = 0, m1 = x1.Average(), m2 = x2.Average(), my = y.Average();
        for (int i = 0; i < n; i++)
        {
            double a = x1[i] - m1, b = x2[i] - m2, c = y[i] - my;
            s11 += a * a; s12 += a * b; s22 += b * b; s1y += a * c; s2y += b * c;
        }
        double det = s11 * s22 - s12 * s12;
        if (Math.Abs(det) < 1e-12) return (my, 0, 0);
        double b1 = (s22 * s1y - s12 * s2y) / det;
        double b2 = (s11 * s2y - s12 * s1y) / det;
        return (my - b1 * m1 - b2 * m2, b1, b2);
    }

    private static double R2(double[] y, double[] pred)
    {
        double my = y.Average(), sst = 0, sse = 0;
        for (int i = 0; i < y.Length; i++) { sst += (y[i] - my) * (y[i] - my); sse += (y[i] - pred[i]) * (y[i] - pred[i]); }
        return sst <= 1e-15 ? 0 : 1.0 - sse / sst;
    }

    private static double Pearson(double[] a, double[] b)
    {
        int n = Math.Min(a.Length, b.Length); if (n < 2) return 0;
        double ma = a.Take(n).Average(), mb = b.Take(n).Average(), sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < n; i++) { double da = a[i] - ma, db = b[i] - mb; sab += da * db; saa += da * da; sbb += db * db; }
        double d = Math.Sqrt(saa * sbb); return d <= 1e-15 ? 0 : sab / d;
    }

    private static double PartialCorr(double[] y, double[] x, double[] z)
    {
        double ryx = Pearson(y, x), ryz = Pearson(y, z), rxz = Pearson(x, z);
        double den = Math.Sqrt((1 - ryz * ryz) * (1 - rxz * rxz));
        return den <= 1e-12 ? 0 : (ryx - ryz * rxz) / den;
    }

    private static double Median(IEnumerable<double> xs)
    {
        double[] v = xs.OrderBy(x => x).ToArray(); if (v.Length == 0) return double.NaN;
        return v.Length % 2 == 1 ? v[v.Length / 2] : 0.5 * (v[v.Length / 2 - 1] + v[v.Length / 2]);
    }

    private static double StdDev(IReadOnlyList<double> xs)
    {
        if (xs.Count < 2) return 0; double m = xs.Average();
        return Math.Sqrt(xs.Sum(x => (x - m) * (x - m)) / (xs.Count - 1));
    }

    private static (double mean, double ci95) MeanCi(IReadOnlyList<double> xs)
    {
        if (xs.Count == 0) return (double.NaN, double.NaN);
        double m = xs.Average(); if (xs.Count < 2) return (m, double.NaN);
        double sd = StdDev(xs); return (m, 1.96 * sd / Math.Sqrt(xs.Count));
    }

    private static double MaxDrawdown(IEnumerable<double> perTrade)
    {
        double cum = 0, peak = 0, mdd = 0;
        foreach (double x in perTrade) { cum += x; peak = Math.Max(peak, cum); mdd = Math.Min(mdd, cum - peak); }
        return mdd;
    }

    private void WriteReport()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) return;
        string od = Path.Combine(dir, "Research", "OrderFlowFeasibility", "Output");
        Directory.CreateDirectory(od);
        File.WriteAllText(Path.Combine(od, "deltarange_forward_vol_stop.txt"), string.Join("\n", _log) + "\n");
    }
}
