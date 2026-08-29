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
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Backtest.Risk;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests.Yahoo;
using Xunit;

namespace IQIAIndicator.Tests.Research.StructuralBreakRegimeCost;

/// <summary>
/// QDE-012 Lot 18 — measures the END-TO-END ECONOMIC EFFECT of <c>StructuralBreakRule</c> (the decision
/// rule that produces <see cref="MarketState.StructuralBreak"/> and, per Lot 17, wins ~31% of bars and
/// routes every one of them to NO_ACTION / UNSUPPORTED_REGIME).
///
/// Method: run the UNMODIFIED production backtest (<see cref="BacktestEngine.RunFullBacktestWithRisk"/>)
/// on the shared Yahoo MES M5 dataset TWICE - once as production, once with
/// <see cref="PipelineParameterOverrides.AblateStructuralBreakRegimeRule"/> = true (a research-only,
/// defaulted seam added in Lot 18: it drops ONLY <c>StructuralBreakRule</c> from the DecisionEngine
/// list, other four rules and order unchanged). Everything else - dataset, window, warmup, measurement /
/// execution / risk / P&amp;L configuration - is byte-identical between the two runs, so the delta is
/// attributable to that one rule.
///
/// Non-regression: the flag defaulting to null/false must reproduce production bit-for-bit
/// (RiskResult.DeterministicHash). Determinism: the ablated path must be reproducible.
///
/// READ ONLY on strategy: this draws a conclusion, it does NOT change the production rule list, weights
/// or any threshold. Output CSVs: Tests/Research/StructuralBreakRegimeCost/Output/.
/// </summary>
public sealed class StructuralBreakRegimeCostAuditTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public StructuralBreakRegimeCostAuditTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private const int WarmupBars = 128;

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static (MeasurementConfiguration M, ExecutionConfiguration E, PnLConfiguration P,
        ExecutionCostConfiguration C, BacktestRiskConfiguration R) Configs() => (
        MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 }),
        ExecutionConfiguration.Create(10),
        PnLConfiguration.Create(InstrumentPnLSpecification.Create("MES", priceUnitValue: 5m, currency: "USD"), quantity: 1),
        ExecutionCostConfiguration.Disabled(),
        BacktestRiskConfiguration.Disabled());

    [Fact]
    public void Integration_Network_StructuralBreakRegimeRule_EndToEnd_EconomicCost_Audit()
    {
        try
        {
            HistoricalSeries series = _yahoo.Require();
            Assert.True(series.Count > 0);

            string fingerprint = HistoricalSeriesFingerprint.Compute(series);
            _output.WriteLine("=== DATASET IDENTITY ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}, Fingerprint={fingerprint}");

            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("SB-L18-FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            (MeasurementConfiguration m, ExecutionConfiguration e, PnLConfiguration p,
                ExecutionCostConfiguration c, BacktestRiskConfiguration r) = Configs();

            var engine = new BacktestEngine();

            // Run A — production (7-arg overload => PipelineParameterOverrides.None).
            BacktestFullResultWithRisk prod = engine.RunFullBacktestWithRisk(scenario, WarmupBars, m, e, p, c, r);

            // Run B — explicit flag = false : must be bit-for-bit identical to production.
            BacktestFullResultWithRisk flagOff = engine.RunFullBacktestWithRisk(
                scenario, WarmupBars, m, e, p, c, r,
                new PipelineParameterOverrides { AblateStructuralBreakRegimeRule = false });
            Assert.Equal(prod.RiskResult.DeterministicHash, flagOff.RiskResult.DeterministicHash);

            // Run C — StructuralBreakRule ABLATED.
            BacktestFullResultWithRisk abl = engine.RunFullBacktestWithRisk(
                scenario, WarmupBars, m, e, p, c, r,
                new PipelineParameterOverrides { AblateStructuralBreakRegimeRule = true });

            // Run D — ablated again : the ablated path must be deterministic.
            BacktestFullResultWithRisk abl2 = engine.RunFullBacktestWithRisk(
                scenario, WarmupBars, m, e, p, c, r,
                new PipelineParameterOverrides { AblateStructuralBreakRegimeRule = true });
            Assert.Equal(abl.RiskResult.DeterministicHash, abl2.RiskResult.DeterministicHash);

            string outDir = ResolveOutputDirectory();
            string h1 = Emit(prod, abl, outDir, print: true);
            string h2 = Emit(prod, abl, outDir, print: false);
            _output.WriteLine($"=== DETERMINISM (report agg) === Hash1={h1} Hash2={h2} Identical={h1 == h2}");
            Assert.Equal(h1, h2);

            // Sanity: ablating the rule DOES change the traded set (both adds and removes positions -
            // it reassigns the ~31% StructuralBreak bars AND recomputes AmbiguityScore for MeanReverting
            // bars where StructuralBreak was the runner-up, per Lot 17). We only assert it is NOT a no-op.
            Assert.NotEqual(prod.RiskResult.DeterministicHash, abl.RiskResult.DeterministicHash);
        }
        catch (Exception exception) when (exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private string Emit(BacktestFullResultWithRisk prod, BacktestFullResultWithRisk abl, string outDir, bool print)
    {
        var hash = new StringBuilder();

        // ---- 1. Signal funnel (both runs) -----------------------------------------------------------
        (int ready, int buy, int sell, int noAction, int watch) F(BacktestFullResultWithRisk x)
        {
            int ready = 0, buy = 0, sell = 0, noAction = 0, watch = 0;
            foreach (BacktestSignalResult b in x.SignalResult.Bars)
            {
                if (b.Status != BacktestSignalStatus.Ready) continue;
                ready++;
                string dir = b.EntryTrigger?.Assessment.Direction.ToString() ?? "n/a";
                if (dir == "BUY_CANDIDATE") buy++;
                else if (dir == "SELL_CANDIDATE") sell++;
                else if (dir == "WATCH") watch++;
                else noAction++;
            }
            return (ready, buy, sell, noAction, watch);
        }
        var fp = F(prod);
        var fa = F(abl);

        (decimal net, decimal dd, int closed, int wins, int losses, int flat) E(BacktestFullResultWithRisk x)
        {
            List<PositionRiskOutcome> closedOutcomes = x.RiskResult.Outcomes
                .Where(o => o.Status == PositionStatus.Closed && o.NetPnL is not null).ToList();
            int wins = closedOutcomes.Count(o => o.NetPnL > 0m);
            int losses = closedOutcomes.Count(o => o.NetPnL < 0m);
            int flat = closedOutcomes.Count(o => o.NetPnL == 0m);
            return (x.RiskResult.FinalNetPnL, x.RiskResult.MaximumDrawdown, closedOutcomes.Count, wins, losses, flat);
        }
        var ep = E(prod);
        var ea = E(abl);

        var summary = new List<object?[]>
        {
            new object?[] { "Metric", "Production(5 rules)", "Ablated(no StructuralBreakRule)", "Delta (abl - prod)" },
            new object?[] { "ReadyBars", fp.ready, fa.ready, fa.ready - fp.ready },
            new object?[] { "BUY_CANDIDATE", fp.buy, fa.buy, fa.buy - fp.buy },
            new object?[] { "SELL_CANDIDATE", fp.sell, fa.sell, fa.sell - fp.sell },
            new object?[] { "NO_ACTION", fp.noAction, fa.noAction, fa.noAction - fp.noAction },
            new object?[] { "ClosedPositions", ep.closed, ea.closed, ea.closed - ep.closed },
            new object?[] { "Wins", ep.wins, ea.wins, ea.wins - ep.wins },
            new object?[] { "Losses", ep.losses, ea.losses, ea.losses - ep.losses },
            new object?[] { "WinRate%", Rate(ep.wins, ep.closed), Rate(ea.wins, ea.closed), null },
            new object?[] { "FinalNetPnL", ep.net, ea.net, ea.net - ep.net },
            new object?[] { "MaximumDrawdown", ep.dd, ea.dd, ea.dd - ep.dd },
        };
        WriteCsv(Path.Combine(outDir, "sb_regime_cost_summary.csv"),
            new[] { "col0", "col1", "col2", "col3" }, summary, headerFromFirstRow: true);
        AppendHash(hash, summary);

        // ---- 2. Positions ADDED / REMOVED by the ablation ----------------------------------------------
        // PositionId == the SIGNAL bar index (see BacktestRiskResultBuilder / the AmbiguityGate lot's own
        // regimeMap keyed on o.PositionId). Bar indices are stable across the two runs (same series), so
        // matching on PositionId is the faithful "same bar" join. Production per-bar regime winner:
        Dictionary<int, MarketState> prodWinnerByBar = prod.SignalResult.Bars
            .Where(b => b.Status == BacktestSignalStatus.Ready && b.Decision is not null)
            .ToDictionary(b => b.BarIndex, b => b.Decision!.Winner);

        List<PositionRiskOutcome> ProdClosed(BacktestFullResultWithRisk x) => x.RiskResult.Outcomes
            .Where(o => o.Status == PositionStatus.Closed && o.NetPnL is not null).ToList();
        var prodIds = ProdClosed(prod).Select(o => o.PositionId).ToHashSet();
        var ablIds = ProdClosed(abl).Select(o => o.PositionId).ToHashSet();

        List<PositionRiskOutcome> added = ProdClosed(abl).Where(o => !prodIds.Contains(o.PositionId)).ToList();
        List<PositionRiskOutcome> removed = ProdClosed(prod).Where(o => !ablIds.Contains(o.PositionId)).ToList();

        int addedFromSb = added.Count(o => prodWinnerByBar.TryGetValue(o.PositionId, out MarketState w) && w == MarketState.StructuralBreak);
        decimal addedFromSbNet = added.Where(o => prodWinnerByBar.TryGetValue(o.PositionId, out MarketState w) && w == MarketState.StructuralBreak).Sum(o => o.NetPnL!.Value);
        int addedFromSbWins = added.Count(o => prodWinnerByBar.TryGetValue(o.PositionId, out MarketState w) && w == MarketState.StructuralBreak && o.NetPnL > 0m);
        decimal addedNet = added.Sum(o => o.NetPnL!.Value);
        decimal removedNet = removed.Sum(o => o.NetPnL!.Value);

        var churn = new List<object?[]>
        {
            new object?[] { "Positions ADDED by removing StructuralBreakRule", added.Count, null, R(addedNet) },
            new object?[] { "  ...whose signal bar was a StructuralBreak winner in production", addedFromSb, Rate(addedFromSb, Math.Max(1, added.Count)), R(addedFromSbNet) },
            new object?[] { "     ...their win rate %", null, Rate(addedFromSbWins, Math.Max(1, addedFromSb)), null },
            new object?[] { "Positions REMOVED by removing StructuralBreakRule (ambiguity-gate churn)", removed.Count, null, R(removedNet) },
            new object?[] { "Net traded-set change (added.NetPnL - removed.NetPnL)", null, null, R(addedNet - removedNet) },
            new object?[] { "=> economic effect of KEEPING StructuralBreakRule (prod - ablated FinalNetPnL)", null, null, R(prod.RiskResult.FinalNetPnL - abl.RiskResult.FinalNetPnL) },
        };
        WriteCsv(Path.Combine(outDir, "sb_regime_cost_position_churn.csv"),
            new[] { "Metric", "Count", "Rate%", "NetPnL" }, churn);
        AppendHash(hash, churn);

        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== SUMMARY (production 5-rule vs ablated 4-rule) ===");
            foreach (object?[] row in summary) _output.WriteLine(string.Join(" | ", row.Select(FormatCell)));
            _output.WriteLine("");
            _output.WriteLine("=== POSITION CHURN FROM REMOVING StructuralBreakRule ===");
            foreach (object?[] row in churn) _output.WriteLine(string.Join(" | ", row.Select(FormatCell)));
            _output.WriteLine("");
            _output.WriteLine($"Production FinalNetPnL={prod.RiskResult.FinalNetPnL}  |  Ablated FinalNetPnL={abl.RiskResult.FinalNetPnL}");
            _output.WriteLine($"Production positions(closed)={ep.closed}  |  Ablated positions(closed)={ea.closed}");
            _output.WriteLine($"Production RiskHash={prod.RiskResult.DeterministicHash}");
            _output.WriteLine($"Ablated    RiskHash={abl.RiskResult.DeterministicHash}");
        }

        return Sha256Hex(hash.ToString());
    }

    private static double Rate(int part, int total) => total > 0 ? Math.Round(100.0 * part / total, 3) : 0.0;

    private static decimal R(decimal x) => Math.Round(x, 4);

    private static string ResolveOutputDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) throw new InvalidOperationException("Could not locate IQIAIndicator.Tests.csproj.");
        string outDir = Path.Combine(dir, "Research", "StructuralBreakRegimeCost", "Output");
        Directory.CreateDirectory(outDir);
        return outDir;
    }

    private static void WriteCsv(string path, string[] headers, List<object?[]> rows, bool headerFromFirstRow = false)
    {
        var sb = new StringBuilder();
        IEnumerable<object?[]> body = rows;
        if (headerFromFirstRow && rows.Count > 0)
        {
            sb.AppendLine(string.Join(",", rows[0].Select(CsvCell)));
            body = rows.Skip(1);
        }
        else
        {
            sb.AppendLine(string.Join(",", headers.Select(CsvCell)));
        }
        foreach (object?[] row in body)
            sb.AppendLine(string.Join(",", row.Select(CsvCell)));
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
