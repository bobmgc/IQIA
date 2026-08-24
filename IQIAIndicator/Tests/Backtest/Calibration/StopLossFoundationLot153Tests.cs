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
using Xunit;
using Xunit.Abstractions;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 15.3, task brief §7). Re-measures the real ~59-day Yahoo MES M5 dataset used by
/// Lot 15.0/15.1/15.2 (same <c>Spec()</c>/<c>Policy()</c> fixture pattern, MaxRiskPerTradePercent=0.02m,
/// warmupBars=128) through <see cref="BacktestEngine.RunSignalPipeline"/> - now, for the first time, with
/// this lot's <see cref="Engine.Risk.VolatilityStopLossModel"/> wired in. Lot 15.0's own
/// RegimeCoverageMaturityAuditLot150Tests recorded 2305 directional MeanReverting bars, 100% SIGNAL_ONLY,
/// 0% PLAN_READY (regime_signal_funnel.csv row 2, checked into this same Output/ directory) - the P0
/// finding this lot exists to close. This test is the "after" measurement: it does not repeat Lot 15.0's
/// full regime audit (that file is unmodified, still runs, still reports its own numbers), it only adds
/// the StopLoss/TradePlanStatus/StopDistance/PositionSize/RiskAmount numbers specific to this lot's change.
///
/// AUDIT-ONLY, OBSERVATION ONLY (same discipline as Lot 15.0): reports numbers, draws no PnL/calibration
/// conclusion, tunes nothing. PROTECTED FILES: never edits BacktestEngine/VolatilityStopLossModel/
/// TradePlanBuilder/RiskEngine/RiskPolicy - only constructs/calls them exactly as BacktestEngine's own
/// production code does. Purely additive: this file plus its own new CSV are the only things created -
/// no existing Output/ CSV is overwritten.
/// </summary>
public sealed class StopLossFoundationLot153Tests
{
    private readonly ITestOutputHelper _output;

    public StopLossFoundationLot153Tests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Integration_Network_StopLossFoundation_Lot153()
    {
        try
        {
            // ── Step 1: load the real dataset (exact same call shape as Lot 15.0/15.1/15.2) ──────────────
            var source = new YahooHistoricalBarSource();
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-YahooHistoricalBarSource.DefaultMaxChunkSpanDays);

            HistoricalSeries series = source.Load("MES", "M5", from, to);
            Assert.True(series.Count > 0);

            const int warmupBars = 128;
            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT15.3-STOPLOSS", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            _output.WriteLine("=== DATASET IDENTITY (Lot 15.3) ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");

            // ── Step 2: run the production signal pipeline exactly once (StopLoss is resolved inline by
            // BacktestEngine.RunSignalPipeline for every directional bar - see that file's TradePlan block) ──
            BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(scenario, warmupBars);

            _output.WriteLine("");
            _output.WriteLine($"BarsProcessed={result.BarsProcessed}, ReadyBars={result.ReadyBars}, ExceptionCount={result.ExceptionCount}, BarsRejected={result.BarsRejected}");
            Assert.Equal(0, result.ExceptionCount);

            // ── Step 3: classify every Ready bar by regime + direction ──────────────────────────────────
            var meanRevertingDirectional = new List<BacktestSignalResult>();
            var nonMeanRevertingWithTradePlan = new List<BacktestSignalResult>();

            foreach (BacktestSignalResult bar in result.Bars)
            {
                if (bar.Status != BacktestSignalStatus.Ready) continue;
                if (bar.Decision is null || bar.EntryTrigger is null) continue;

                bool isDirectional = bar.EntryTrigger.Assessment.Direction is DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE;

                if (bar.Decision.Winner == MarketState.MeanReverting && isDirectional)
                    meanRevertingDirectional.Add(bar);
                else if (bar.Decision.Winner != MarketState.MeanReverting)
                    nonMeanRevertingWithTradePlan.Add(bar);
            }

            _output.WriteLine($"Directional MeanReverting bars = {meanRevertingDirectional.Count} (Lot 15.0 baseline: 2305 SIGNAL_ONLY / 0 PLAN_READY, from regime_signal_funnel.csv)");

            // ── Step 4: PLAN_READY must remain empirically zero for every non-MeanReverting regime ───────
            int nonMeanRevertingPlanReady = nonMeanRevertingWithTradePlan.Count(b => b.TradePlan?.Status == TradePlanStatus.PLAN_READY);
            _output.WriteLine($"Non-MeanReverting bars checked = {nonMeanRevertingWithTradePlan.Count}, of which PLAN_READY = {nonMeanRevertingPlanReady} (must be 0 - structurally impossible per Lot 15.1: Direction is BUY/SELL only when Winner==MeanReverting).");
            Assert.Equal(0, nonMeanRevertingPlanReady);

            // ── Step 5: TradePlanStatus distribution over directional MeanReverting bars (the key
            // before/after number: Lot 15.0 was 100% SIGNAL_ONLY / 0% PLAN_READY for these same 2305 bars) ──
            int n = meanRevertingDirectional.Count;
            int noTrade = meanRevertingDirectional.Count(b => b.TradePlan?.Status == TradePlanStatus.NO_TRADE);
            int signalOnly = meanRevertingDirectional.Count(b => b.TradePlan?.Status == TradePlanStatus.SIGNAL_ONLY);
            int planReady = meanRevertingDirectional.Count(b => b.TradePlan?.Status == TradePlanStatus.PLAN_READY);
            int planBlocked = meanRevertingDirectional.Count(b => b.TradePlan?.Status == TradePlanStatus.PLAN_BLOCKED);

            _output.WriteLine("");
            _output.WriteLine("=== TradePlanStatus distribution (directional MeanReverting bars) ===");
            _output.WriteLine($"NO_TRADE={noTrade} ({Pct(noTrade, n)}%), SIGNAL_ONLY={signalOnly} ({Pct(signalOnly, n)}%), PLAN_READY={planReady} ({Pct(planReady, n)}%), PLAN_BLOCKED={planBlocked} ({Pct(planBlocked, n)}%)");
            Assert.Equal(n, noTrade + signalOnly + planReady + planBlocked);

            // ── Step 6: StopLoss resolution rate + null-reason breakdown ────────────────────────────────
            int stopLossResolved = meanRevertingDirectional.Count(b => b.TradePlan?.StopLoss is not null);
            int stopLossNull = n - stopLossResolved;
            _output.WriteLine("");
            _output.WriteLine($"StopLoss resolved (non-null) = {stopLossResolved} ({Pct(stopLossResolved, n)}%), null = {stopLossNull} ({Pct(stopLossNull, n)}%)");

            var nullReasonCounts = new Dictionary<string, int>();
            foreach (BacktestSignalResult bar in meanRevertingDirectional)
            {
                if (bar.TradePlan?.StopLoss is not null) continue;
                string reason = ClassifyNullStopLossReason(bar);
                nullReasonCounts[reason] = nullReasonCounts.GetValueOrDefault(reason) + 1;
            }
            _output.WriteLine("Null-StopLoss reason breakdown:");
            foreach (var kv in nullReasonCounts.OrderByDescending(kv => kv.Value))
                _output.WriteLine($"  {kv.Key} = {kv.Value} ({Pct(kv.Value, Math.Max(stopLossNull, 1))}%)");

            // ── Step 7: StopDistance descriptive stats (price units + ticks), PLAN_READY only ────────────
            List<(decimal Distance, decimal DistanceTicks)> distances = meanRevertingDirectional
                .Where(b => b.TradePlan is { Status: TradePlanStatus.PLAN_READY, StopLoss: not null, EntryPrice: not null })
                .Select(b =>
                {
                    decimal entry = b.TradePlan!.EntryPrice!.Value;
                    decimal stop = b.TradePlan.StopLoss!.Value;
                    decimal distance = b.TradePlan.Direction == DirectionCandidate.BUY_CANDIDATE ? entry - stop : stop - entry;
                    return (distance, distance / Spec().TickSize);
                })
                .ToList();

            List<int> positionSizes = meanRevertingDirectional
                .Where(b => b.TradePlan is { Status: TradePlanStatus.PLAN_READY, PositionSize: not null })
                .Select(b => b.TradePlan!.PositionSize!.Value)
                .ToList();

            List<decimal> riskAmounts = meanRevertingDirectional
                .Where(b => b.TradePlan is { Status: TradePlanStatus.PLAN_READY, RiskAmount: not null })
                .Select(b => b.TradePlan!.RiskAmount!.Value)
                .ToList();

            _output.WriteLine("");
            _output.WriteLine($"=== PLAN_READY descriptive stats (N={distances.Count}) ===");
            if (distances.Count > 0)
            {
                _output.WriteLine($"StopDistance (price units): Mean={Math.Round(distances.Average(d => d.Distance), 6)}, Min={distances.Min(d => d.Distance)}, Max={distances.Max(d => d.Distance)}, Median={Median(distances.Select(d => d.Distance).ToList())}");
                _output.WriteLine($"StopDistance (ticks): Mean={Math.Round(distances.Average(d => d.DistanceTicks), 6)}, Min={distances.Min(d => d.DistanceTicks)}, Max={distances.Max(d => d.DistanceTicks)}, Median={Median(distances.Select(d => d.DistanceTicks).ToList())}");
            }
            if (positionSizes.Count > 0)
                _output.WriteLine($"PositionSize: Mean={Math.Round(positionSizes.Average(), 6)}, Min={positionSizes.Min()}, Max={positionSizes.Max()}");
            if (riskAmounts.Count > 0)
                _output.WriteLine($"RiskAmount: Mean={Math.Round(riskAmounts.Average(), 6)}, Min={riskAmounts.Min()}, Max={riskAmounts.Max()}");

            // Sanity: every resolved StopDistance/RiskAmount/PositionSize must be strictly positive - never
            // fabricated/degenerate for a PLAN_READY bar (TradePlanBuilder's own contract, unmodified by this lot).
            Assert.True(distances.All(d => d.Distance > 0m), "Every PLAN_READY StopDistance must be strictly positive.");
            Assert.True(positionSizes.All(p => p > 0), "Every PLAN_READY PositionSize must be strictly positive.");
            Assert.True(riskAmounts.All(r => r > 0m), "Every PLAN_READY RiskAmount must be strictly positive.");

            // ── Step 8: write CSV (additive - new file, never overwrites an existing Output/ CSV) ─────────
            string outputDir = ResolveOutputDirectory();
            string csvPath = Path.Combine(outputDir, "stoploss_foundation_lot153.csv");

            var rows = new List<object?[]>
            {
                new object?[] { "DirectionalMeanRevertingBars", n, "" },
                new object?[] { "TradePlanNO_TRADE", noTrade, Pct(noTrade, n) },
                new object?[] { "TradePlanSIGNAL_ONLY", signalOnly, Pct(signalOnly, n) },
                new object?[] { "TradePlanPLAN_READY", planReady, Pct(planReady, n) },
                new object?[] { "TradePlanPLAN_BLOCKED", planBlocked, Pct(planBlocked, n) },
                new object?[] { "StopLossResolved", stopLossResolved, Pct(stopLossResolved, n) },
                new object?[] { "StopLossNull", stopLossNull, Pct(stopLossNull, n) },
                new object?[] { "NonMeanRevertingBarsChecked", nonMeanRevertingWithTradePlan.Count, "" },
                new object?[] { "NonMeanRevertingPlanReady", nonMeanRevertingPlanReady, "" },
                new object?[] { "PlanReadyCount_ForStats", distances.Count, "" },
                new object?[] { "StopDistance_Price_Mean", distances.Count > 0 ? Math.Round(distances.Average(d => d.Distance), 6) : (decimal?)null, "" },
                new object?[] { "StopDistance_Price_Min", distances.Count > 0 ? distances.Min(d => d.Distance) : (decimal?)null, "" },
                new object?[] { "StopDistance_Price_Max", distances.Count > 0 ? distances.Max(d => d.Distance) : (decimal?)null, "" },
                new object?[] { "StopDistance_Price_Median", distances.Count > 0 ? Median(distances.Select(d => d.Distance).ToList()) : (decimal?)null, "" },
                new object?[] { "StopDistance_Ticks_Mean", distances.Count > 0 ? Math.Round(distances.Average(d => d.DistanceTicks), 6) : (decimal?)null, "" },
                new object?[] { "StopDistance_Ticks_Min", distances.Count > 0 ? distances.Min(d => d.DistanceTicks) : (decimal?)null, "" },
                new object?[] { "StopDistance_Ticks_Max", distances.Count > 0 ? distances.Max(d => d.DistanceTicks) : (decimal?)null, "" },
                new object?[] { "PositionSize_Mean", positionSizes.Count > 0 ? Math.Round(positionSizes.Average(), 6) : (double?)null, "" },
                new object?[] { "PositionSize_Min", positionSizes.Count > 0 ? positionSizes.Min() : (int?)null, "" },
                new object?[] { "PositionSize_Max", positionSizes.Count > 0 ? positionSizes.Max() : (int?)null, "" },
                new object?[] { "RiskAmount_Mean", riskAmounts.Count > 0 ? Math.Round(riskAmounts.Average(), 6) : (decimal?)null, "" },
                new object?[] { "RiskAmount_Min", riskAmounts.Count > 0 ? riskAmounts.Min() : (decimal?)null, "" },
                new object?[] { "RiskAmount_Max", riskAmounts.Count > 0 ? riskAmounts.Max() : (decimal?)null, "" }
            };
            foreach (var kv in nullReasonCounts.OrderBy(kv => kv.Key))
                rows.Add(new object?[] { $"NullStopLossReason_{kv.Key}", kv.Value, Pct(kv.Value, Math.Max(stopLossNull, 1)) });

            WriteCsv(csvPath, new[] { "Metric", "Value", "Percent" }, rows);
            _output.WriteLine("");
            _output.WriteLine($"CSV written: {csvPath}");
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            _output.WriteLine($"SKIPPED (network/Yahoo unavailable, not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static string ClassifyNullStopLossReason(BacktestSignalResult bar)
    {
        IReadOnlyList<ScientificModelResult>? results =
            bar.EntryTrigger?.EntryCandidate?.Assessment?.ScientificAssessment?.ScientificResults;

        if (results is null)
            return "NoScientificResults";

        ScientificModelResult? volatilityResult = results.FirstOrDefault(r => string.Equals(r.ModelName, "VolatilityModel", StringComparison.OrdinalIgnoreCase));
        if (volatilityResult is null)
            return "VolatilityModelMissing";
        if (!volatilityResult.Success)
            return "VolatilityModelUnsuccessful";
        if (volatilityResult.Metrics is null || !volatilityResult.Metrics.TryGetValue(ScientificMetricKeys.CurrentVolatility, out object? raw))
            return "CurrentVolatilityMetricMissing";
        if (raw is not double d || !double.IsFinite(d) || d <= 0.0)
            return "CurrentVolatilityMetricInvalid";
        if (bar.TradePlan?.EntryPrice is not decimal entry || entry <= 0m)
            return "EntryPriceInvalid";

        return "DegenerateAfterTickRounding";
    }

    private static double Pct(int count, int total) => total > 0 ? Math.Round(100.0 * count / total, 3) : 0.0;

    private static decimal Median(List<decimal> values)
    {
        if (values.Count == 0) return 0m;
        List<decimal> sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2m : sorted[mid];
    }

    private static string ResolveOutputDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;

        if (dir is null)
            throw new InvalidOperationException("Could not locate IQIAIndicator.Tests.csproj to resolve the RegimeCoverageAudit output directory.");

        string outputDir = Path.Combine(dir, "Research", "RegimeCoverageAudit", "Output");
        Directory.CreateDirectory(outputDir);
        return outputDir;
    }

    private static void WriteCsv(string path, string[] headers, List<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(CsvCell)));
        foreach (object?[] row in rows)
            sb.AppendLine(string.Join(",", row.Select(CsvCell)));
        File.WriteAllText(path, sb.ToString());
    }

    private static string CsvCell(object? value)
    {
        string s = FormatCell(value);
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            s = "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private static string FormatCell(object? value) => value switch
    {
        null => "",
        double d => d.ToString("G17", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or InvalidOperationException;
}
