using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.Rules;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests.Yahoo;
using Xunit;
using FusionEngine = IQIAIndicator.Engine.Fusion.EvidenceFusionEngine;
using FusionRandomWalkRule = IQIAIndicator.Engine.Fusion.Rules.RandomWalkRule;
using DecisionRandomWalkRule = IQIAIndicator.Engine.Decision.Rules.RandomWalkRule;

namespace IQIAIndicator.Tests.Research.StructuralBreakSignalContribution;

/// <summary>
/// QDE-012 Lot 17 — END-TO-END AUDIT of the REAL contribution of "StructuralBreak" (both the
/// <see cref="FusionDimension.StructuralBreak"/> evidence dimension AND the
/// <see cref="MarketState.StructuralBreak"/> regime) all the way to the trading signal.
/// READ ONLY / observation only / additive / deterministic. No production type is modified.
///
/// Method:
///  1. Run the unmodified <see cref="BacktestEngine.RunSignalPipeline"/> over the shared Yahoo MES M5
///     dataset (production wiring, production 5 decision rules).
///  2. Bar-by-bar, reconstruct the stabilized <see cref="FusionResult"/> the production Decision
///     consumed (same 5-rule EvidenceFusionEngine + a fresh FusionStateManager, chronological order -
///     the exact pattern SixDimensionIndependentAuditTests already validated), then re-run the decision
///     through THREE engines over that same FusionResult:
///       - engine5  : production rule list  {StableRange, Trending, MeanReverting, StructuralBreak, RandomWalk}
///       - engine4  : StructuralBreakRule ABLATED  {StableRange, Trending, MeanReverting, RandomWalk}
///       - engine5 again but with FusionDimension.StructuralBreak forced to 0.0 and to 1.0
///  3. Measure:
///       A. Reconstruction fidelity (engine5 winner == production winner).
///       B. D6 non-consumption: winner/score identical when FusionDimension.StructuralBreak is forced
///          to 0 or 1  ->  empirical proof the evidence dimension has zero decision effect.
///       C. Ablation transition matrix: production winner -> engine4 winner.
///       D. Counterfactual signal gain: StructuralBreak-winner bars that become MeanReverting when the
///          rule is ablated, and how many of those pass the 0.95 ambiguity gate (upper bound on the
///          directional signals StructuralBreakRule currently suppresses).
///       E. Cross-contamination: bars whose winner is unchanged by the ablation but whose ambiguity
///          moved across the 0.95 gate (StructuralBreak was the runner-up).
///       F. Correlation between the evidence dimension value and being the StructuralBreak regime winner.
/// Outputs: Tests/Research/StructuralBreakSignalContribution/Output/*.csv
/// </summary>
public sealed class StructuralBreakSignalContributionAuditTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public StructuralBreakSignalContributionAuditTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private const int WarmupBars = 128;
    private const double AmbiguityGate = EntryTriggerBuilder.AmbiguityGateThreshold; // 0.95, production const

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    // Production-equivalent lists (order-sensitive: arbitration is highest-FinalScore-wins, but the
    // running builder.Confidence in each rule is order-dependent, so the order below MIRRORS
    // IQIAIndicator.cs exactly).
    private static FusionEngine ProductionFusion() => new(new IFusionRule[]
    {
        new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new FusionRandomWalkRule(),
        new StructuralBreakEvidenceRule()
    });

    private static DecisionEngine Engine5() => new(new IDecisionRule[]
    {
        new StableRangeRule(), new TrendingRule(), new MeanRevertingRule(),
        new StructuralBreakRule(), new DecisionRandomWalkRule()
    });

    private static DecisionEngine Engine4NoStructuralBreak() => new(new IDecisionRule[]
    {
        new StableRangeRule(), new TrendingRule(), new MeanRevertingRule(), new DecisionRandomWalkRule()
    });

    private sealed record Row(
        int BarIndex,
        MarketState ProdWinner, double ProdAmbiguity,
        MarketState Recon5Winner, double Recon5Ambiguity,
        MarketState Abl4Winner, double Abl4Ambiguity,
        string ProdDirection, string ProdReason,
        double D6Value, bool D6Available,
        bool D6PerturbationChangedWinner);

    [Fact]
    public void Integration_Network_StructuralBreak_EndToEnd_SignalContribution_Audit()
    {
        try
        {
            HistoricalSeries series = _yahoo.Require();
            Assert.True(series.Count > 0);

            string fingerprint = HistoricalSeriesFingerprint.Compute(series);
            _output.WriteLine("=== DATASET IDENTITY ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}, Fingerprint={fingerprint}");

            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("SB-L17-FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            BacktestSignalPipelineResult signal = new BacktestEngine().RunSignalPipeline(scenario, WarmupBars);
            Assert.Equal(0, signal.ExceptionCount);
            _output.WriteLine($"BarsProcessed={signal.BarsProcessed}, ReadyBars={signal.ReadyBars}, WarmupBars={signal.WarmupBars}");

            FusionEngine fusion = ProductionFusion();
            var fusionState = new FusionStateManager();
            DecisionEngine engine5 = Engine5();
            DecisionEngine engine4 = Engine4NoStructuralBreak();

            var rows = new List<Row>(series.Count);
            int fidelityMismatch = 0;

            foreach (BacktestSignalResult bar in signal.Bars)
            {
                if (bar.Status is BacktestSignalStatus.Rejected or BacktestSignalStatus.Exception) continue;
                if (bar.Regime is null) continue;

                FusionResult raw = fusion.Fuse(new FusionContext
                {
                    Evidence = bar.Regime, Timestamp = bar.Timestamp, Symbol = series.Symbol,
                    TimeFrame = series.TimeFrame, EvaluationId = Guid.Empty
                });
                FusionSnapshot snap = fusionState.Update(raw, bar.Timestamp);

                if (bar.Status != BacktestSignalStatus.Ready || bar.Decision is null) continue;

                FusionResult stable = snap.StableResult;
                var ctx = new DecisionContext { FusionResult = stable, Evidence = bar.Regime };

                DecisionResult d5 = engine5.Evaluate(ctx);
                DecisionResult d4 = engine4.Evaluate(ctx);

                // B — force the (unused?) evidence dimension to the two extremes and see if anything moves.
                FusionConfidence d6 = stable.Dimensions.TryGetValue(FusionDimension.StructuralBreak, out FusionConfidence? c)
                    ? c
                    : new FusionConfidence { Value = 0.0, Confidence = 0.0, IsAvailable = false };

                DecisionResult d5Zero = engine5.Evaluate(new DecisionContext
                {
                    Evidence = bar.Regime,
                    FusionResult = stable with
                    {
                        Dimensions = stable.Dimensions.SetItem(
                            FusionDimension.StructuralBreak,
                            new FusionConfidence { Value = 0.0, Confidence = 0.0, IsAvailable = true })
                    }
                });
                DecisionResult d5One = engine5.Evaluate(new DecisionContext
                {
                    Evidence = bar.Regime,
                    FusionResult = stable with
                    {
                        Dimensions = stable.Dimensions.SetItem(
                            FusionDimension.StructuralBreak,
                            new FusionConfidence { Value = 1.0, Confidence = 1.0, IsAvailable = true })
                    }
                });
                bool d6PerturbationChangedWinner =
                    d5Zero.Winner != d5.Winner || d5One.Winner != d5.Winner ||
                    Math.Abs(d5Zero.WinnerScore - d5.WinnerScore) > 1e-12 ||
                    Math.Abs(d5One.WinnerScore - d5.WinnerScore) > 1e-12;

                if (d5.Winner != bar.Decision.Winner) fidelityMismatch++;

                rows.Add(new Row(
                    bar.BarIndex,
                    bar.Decision.Winner, bar.Decision.AmbiguityScore,
                    d5.Winner, d5.AmbiguityScore,
                    d4.Winner, d4.AmbiguityScore,
                    bar.EntryTrigger?.Assessment.Direction.ToString() ?? "n/a",
                    bar.EntryTrigger?.Assessment.Reason.ToString() ?? "n/a",
                    d6.Value, d6.IsAvailable,
                    d6PerturbationChangedWinner));
            }

            Assert.Equal(signal.ReadyBars, rows.Count);
            _output.WriteLine($"ObservedReadyBars={rows.Count}");

            string outDir = ResolveOutputDirectory();
            string h1 = Emit(rows, fidelityMismatch, outDir, print: true);
            string h2 = Emit(rows, fidelityMismatch, outDir, print: false);
            _output.WriteLine($"=== DETERMINISM === Hash1={h1} Hash2={h2} Identical={h1 == h2}");
            Assert.Equal(h1, h2);

            // Hard invariants (the audit's conclusions, asserted):
            //  - reconstruction is faithful
            Assert.True(fidelityMismatch <= rows.Count / 1000 + 1,
                $"reconstruction fidelity: {fidelityMismatch}/{rows.Count} engine5 winners differ from production");
            //  - FusionDimension.StructuralBreak (D6) has ZERO effect on the decision
            int d6Effect = rows.Count(r => r.D6PerturbationChangedWinner);
            Assert.Equal(0, d6Effect);
            //  - no production bar with winner==StructuralBreak ever produced a BUY/SELL
            Assert.DoesNotContain(rows, r => r.ProdWinner == MarketState.StructuralBreak
                && (r.ProdDirection == "BUY_CANDIDATE" || r.ProdDirection == "SELL_CANDIDATE"));
        }
        catch (Exception exception) when (exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private string Emit(List<Row> rows, int fidelityMismatch, string outDir, bool print)
    {
        var hash = new StringBuilder();
        int n = rows.Count;

        // ---- 1. End-to-end funnel by production regime -------------------------------------------------
        var funnel = new List<object?[]>();
        foreach (MarketState regime in Enum.GetValues<MarketState>())
        {
            List<Row> g = rows.Where(r => r.ProdWinner == regime).ToList();
            if (g.Count == 0) continue;
            funnel.Add(new object?[]
            {
                regime, g.Count, Pct(g.Count, n),
                g.Count(r => r.ProdDirection == "BUY_CANDIDATE"),
                g.Count(r => r.ProdDirection == "SELL_CANDIDATE"),
                g.Count(r => r.ProdDirection == "NO_ACTION"),
                g.Count(r => r.ProdDirection == "WATCH"),
                g.Count(r => r.ProdReason == "UNSUPPORTED_REGIME"),
                g.Count(r => r.ProdReason == "DECISION_AMBIGUOUS"),
                R(g.Average(r => r.ProdAmbiguity)),
                g.Count(r => r.ProdAmbiguity < AmbiguityGate),
            });
        }
        WriteCsv(Path.Combine(outDir, "sb_end_to_end_funnel.csv"),
            new[] { "ProdWinner", "Bars", "PctOfReady", "BUY", "SELL", "NO_ACTION", "WATCH",
                "Reason_UNSUPPORTED_REGIME", "Reason_DECISION_AMBIGUOUS", "MeanAmbiguity", "AmbiguityBelowGate" },
            funnel);
        AppendHash(hash, funnel);

        // ---- 2. Ablation transition matrix: prod winner -> engine4 (no StructuralBreakRule) -----------
        var trans = new List<object?[]>();
        foreach (MarketState from in Enum.GetValues<MarketState>())
        {
            List<Row> g = rows.Where(r => r.ProdWinner == from).ToList();
            if (g.Count == 0) continue;
            foreach (MarketState to in Enum.GetValues<MarketState>())
            {
                int cnt = g.Count(r => r.Abl4Winner == to);
                if (cnt == 0) continue;
                trans.Add(new object?[] { from, to, cnt, Pct(cnt, g.Count) });
            }
        }
        WriteCsv(Path.Combine(outDir, "sb_ablation_transition.csv"),
            new[] { "ProdWinner_5rule", "AblatedWinner_4rule", "Bars", "PctOfProdWinnerGroup" }, trans);
        AppendHash(hash, trans);

        // ---- 3. Counterfactual: what signals does StructuralBreakRule currently SUPPRESS? -------------
        List<Row> sbWinners = rows.Where(r => r.ProdWinner == MarketState.StructuralBreak).ToList();
        int sbToMeanRev = sbWinners.Count(r => r.Abl4Winner == MarketState.MeanReverting);
        int sbToMeanRevGatePass = sbWinners.Count(r => r.Abl4Winner == MarketState.MeanReverting && r.Abl4Ambiguity < AmbiguityGate);
        int sbToOther = sbWinners.Count - sbWinners.Count(r => r.Abl4Winner == MarketState.MeanReverting);

        // Cross-contamination: winner unchanged by ablation but ambiguity crossed the gate.
        int ambiguityGateFlips = rows.Count(r =>
            r.ProdWinner == r.Abl4Winner &&
            (r.ProdAmbiguity < AmbiguityGate) != (r.Abl4Ambiguity < AmbiguityGate));
        int gateFlipsAmongMeanRev = rows.Count(r =>
            r.ProdWinner == MarketState.MeanReverting && r.Abl4Winner == MarketState.MeanReverting &&
            (r.ProdAmbiguity < AmbiguityGate) != (r.Abl4Ambiguity < AmbiguityGate));

        var counter = new List<object?[]>
        {
            new object?[] { "StructuralBreak_winner_bars", sbWinners.Count, Pct(sbWinners.Count, n) },
            new object?[] { "  -> MeanReverting when rule ablated", sbToMeanRev, Pct(sbToMeanRev, Math.Max(1, sbWinners.Count)) },
            new object?[] { "     ...of which ambiguity < gate (reach zscore direction logic)", sbToMeanRevGatePass, Pct(sbToMeanRevGatePass, Math.Max(1, sbWinners.Count)) },
            new object?[] { "  -> a non-tradeable regime when ablated", sbToOther, Pct(sbToOther, Math.Max(1, sbWinners.Count)) },
            new object?[] { "ambiguity-gate flips on winner-unchanged bars (SB was runner-up)", ambiguityGateFlips, Pct(ambiguityGateFlips, n) },
            new object?[] { "  ...of which on MeanReverting-winner bars", gateFlipsAmongMeanRev, Pct(gateFlipsAmongMeanRev, n) },
            new object?[] { "D6 perturbation (force StructuralBreak dim to 0 and 1) changed a decision", rows.Count(r => r.D6PerturbationChangedWinner), null },
            new object?[] { "reconstruction fidelity mismatch (engine5 winner != production)", fidelityMismatch, null },
        };
        WriteCsv(Path.Combine(outDir, "sb_counterfactual.csv"),
            new[] { "Metric", "Count", "Pct" }, counter);
        AppendHash(hash, counter);

        // ---- 4. Correlation: evidence dimension value vs being the StructuralBreak regime winner ------
        List<double> d6 = rows.Select(r => r.D6Value).ToList();
        List<double> isSbWinner = rows.Select(r => r.ProdWinner == MarketState.StructuralBreak ? 1.0 : 0.0).ToList();
        List<double> d6SbWin = rows.Where(r => r.ProdWinner == MarketState.StructuralBreak).Select(r => r.D6Value).ToList();
        List<double> d6NotSbWin = rows.Where(r => r.ProdWinner != MarketState.StructuralBreak).Select(r => r.D6Value).ToList();
        var corr = new List<object?[]>
        {
            new object?[] { "PointBiserial(D6.Value, IsStructuralBreakWinner)", n, R(Pearson(d6, isSbWinner)) },
            new object?[] { "Mean D6.Value | StructuralBreak winner", d6SbWin.Count, d6SbWin.Count > 0 ? R(d6SbWin.Average()) : (double?)null },
            new object?[] { "Mean D6.Value | other winner", d6NotSbWin.Count, d6NotSbWin.Count > 0 ? R(d6NotSbWin.Average()) : (double?)null },
            new object?[] { "D6.IsAvailable rate", n, Pct(rows.Count(r => r.D6Available), n) },
        };
        WriteCsv(Path.Combine(outDir, "sb_evidence_vs_regime_correlation.csv"),
            new[] { "Metric", "N", "Value" }, corr);
        AppendHash(hash, corr);

        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== END-TO-END FUNNEL (by production regime winner) ===");
            _output.WriteLine("ProdWinner | Bars | %Ready | BUY | SELL | NO_ACTION | WATCH | UNSUPPORTED_REGIME | DECISION_AMBIGUOUS | MeanAmbig | Ambig<Gate");
            foreach (object?[] r in funnel) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
            _output.WriteLine("");
            _output.WriteLine("=== ABLATION TRANSITIONS (5-rule prod winner -> 4-rule winner, StructuralBreakRule removed) ===");
            foreach (object?[] r in trans) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
            _output.WriteLine("");
            _output.WriteLine("=== COUNTERFACTUAL (what StructuralBreakRule currently suppresses) ===");
            foreach (object?[] r in counter) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
            _output.WriteLine("");
            _output.WriteLine("=== EVIDENCE DIMENSION vs REGIME LABEL ===");
            foreach (object?[] r in corr) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        return Sha256Hex(hash.ToString());
    }

    // ---- helpers (self-contained, same shapes as SixDimensionIndependentAuditTests) -----------------

    private static double R(double x) => Math.Round(x, 6);
    private static double Pct(int c, int t) => t > 0 ? Math.Round(100.0 * c / t, 3) : 0.0;

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

    private static string ResolveOutputDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) throw new InvalidOperationException("Could not locate IQIAIndicator.Tests.csproj.");
        string outDir = Path.Combine(dir, "Research", "StructuralBreakSignalContribution", "Output");
        Directory.CreateDirectory(outDir);
        return outDir;
    }

    private static void WriteCsv(string path, string[] headers, List<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(CsvCell)));
        foreach (object?[] row in rows)
        {
            var padded = new object?[headers.Length];
            for (int i = 0; i < headers.Length; i++) padded[i] = i < row.Length ? row[i] : null;
            sb.AppendLine(string.Join(",", padded.Select(CsvCell)));
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static string CsvCell(object? value)
    {
        string s = FormatCell(value);
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n')) s = "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private static string FormatCell(object? value) => value switch
    {
        null => "",
        double d => d.ToString("G17", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("O"),
        bool b => b.ToString(),
        _ => value.ToString() ?? ""
    };

    private static void AppendHash(StringBuilder sb, List<object?[]> rows)
    {
        foreach (object?[] row in rows) sb.Append(string.Join("|", row.Select(FormatCell))).Append(';');
    }

    private static string Sha256Hex(string s)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
