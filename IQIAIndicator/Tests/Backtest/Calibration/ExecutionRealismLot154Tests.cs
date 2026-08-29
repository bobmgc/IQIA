using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using Xunit;
using IQIAIndicator.Tests.BacktestTests.Yahoo;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 15.4, brief §28/§29). Re-measures the same real ~59-day Yahoo MES M5 dataset used by
/// Lots 15.0-15.3 (identical <c>Spec()</c>/<c>Policy()</c> fixture, MaxRiskPerTradePercent=0.02m,
/// warmupBars=128) - now through <see cref="BacktestEngine.RunSimulation"/> so Stop Loss/Take Profit are
/// wired into EXECUTION for the first time (Lot 15.3 only wired them into the TradePlan; execution still
/// used pure TIME_HORIZON exits until this lot).
///
/// PURELY DESCRIPTIVE (brief §29, explicitly required): this file reports counts/percentages/holding-bars
/// distributions. It draws NO improvement/regression conclusion and tunes NOTHING - the Lot 15.3 baseline
/// (100% TimeHorizon, since Lot 15.3 never wired Stop/Target into execution) is stated as a reference point
/// only, never as a target. AUDIT-ONLY, same discipline as Lot 15.0/15.3's own calibration test files.
///
/// PROTECTED FILES: never edits BacktestEngine/ExecutionSimulator/RiskEngine/TradePlanBuilder/RiskPolicy -
/// only constructs/calls them exactly as production code does. Purely additive: this file plus its own new
/// CSV are the only things created here - no existing Output/ CSV is overwritten.
/// </summary>
public sealed class ExecutionRealismLot154Tests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public ExecutionRealismLot154Tests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Integration_Network_ExecutionRealism_Lot154()
    {
        try
        {
            // ── Step 1: load the real dataset (exact same recipe as Lot 15.0/15.1/15.2/15.3) ────────────
            HistoricalSeries series = _yahoo.Require();
            Assert.True(series.Count > 0);

            const int warmupBars = 128;
            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT15.4-EXECUTION-REALISM", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            _output.WriteLine("=== DATASET IDENTITY (Lot 15.4) ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");

            // ── Step 2: run the full production simulation once (Measurement horizon/thresholds are
            // irrelevant to this file - Measurement and Execution never share state - kept identical to
            // ExecutionYahooIntegrationTests' own call shape for consistency) ─────────────────────────────
            var executionConfig = ExecutionConfiguration.Create(10);
            BacktestSimulationResult result = new BacktestEngine().RunSimulation(
                scenario, warmupBars, MeasurementConfiguration.Create(10, new[] { 0.001 }), executionConfig);

            _output.WriteLine("");
            _output.WriteLine($"BarsProcessed={result.SignalResult.Bars.Count}, ExceptionCount={result.SignalResult.ExceptionCount}, BarsRejected={result.SignalResult.BarsRejected}");
            Assert.Equal(0, result.SignalResult.ExceptionCount);
            Assert.Equal(series.Count, result.ExecutionResult.Positions.Count);

            // Accounting identity, extended for InvalidStopTargetCount this lot.
            Assert.Equal(result.ExecutionResult.TotalCount,
                result.ExecutionResult.ClosedCount + result.ExecutionResult.NotExecutableCount +
                result.ExecutionResult.InvalidEntryCount + result.ExecutionResult.InsufficientFutureDataCount +
                result.ExecutionResult.InvalidExitCount + result.ExecutionResult.InvalidStopTargetCount);

            // ── Step 3: select MeanReverting directional bars (same classification as Lot 15.0/15.3), then
            // pair each one with its own SimulatedPosition (Positions[i] <-> SignalResult.Bars[i], same index) ──
            var indices = new List<int>();
            for (int i = 0; i < result.SignalResult.Bars.Count; i++)
            {
                BacktestSignalResult bar = result.SignalResult.Bars[i];
                if (bar.Status != BacktestSignalStatus.Ready) continue;
                if (bar.Decision is null || bar.EntryTrigger is null) continue;
                bool isDirectional = bar.EntryTrigger.Assessment.Direction is DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE;
                if (bar.Decision.Winner == MarketState.MeanReverting && isDirectional)
                    indices.Add(i);
            }

            int n = indices.Count;
            _output.WriteLine($"Directional MeanReverting bars = {n} (candidates for execution realism analysis).");

            List<SimulatedPosition> positions = indices.Select(i => result.ExecutionResult.Positions[i]).ToList();

            // ── Step 4: PositionStatus breakdown (pre-execution bucket, includes InvalidStopTarget) ───────
            int closed = positions.Count(p => p.Status == PositionStatus.Closed);
            int notExecutable = positions.Count(p => p.Status == PositionStatus.NotExecutable);
            int invalidEntry = positions.Count(p => p.Status == PositionStatus.InvalidEntry);
            int insufficientFutureData = positions.Count(p => p.Status == PositionStatus.InsufficientFutureData);
            int invalidExit = positions.Count(p => p.Status == PositionStatus.InvalidExit);
            int invalidStopTarget = positions.Count(p => p.Status == PositionStatus.InvalidStopTarget);
            Assert.Equal(n, closed + notExecutable + invalidEntry + insufficientFutureData + invalidExit + invalidStopTarget);

            _output.WriteLine("");
            _output.WriteLine("=== PositionStatus breakdown (MeanReverting directional bars, N={0}) ===", n);
            _output.WriteLine($"Closed={closed} ({Pct(closed, n)}%), NotExecutable={notExecutable} ({Pct(notExecutable, n)}%), " +
                $"InvalidEntry={invalidEntry} ({Pct(invalidEntry, n)}%), InsufficientFutureData={insufficientFutureData} ({Pct(insufficientFutureData, n)}%), " +
                $"InvalidExit={invalidExit} ({Pct(invalidExit, n)}%), InvalidStopTarget={invalidStopTarget} ({Pct(invalidStopTarget, n)}%)");
            _output.WriteLine($"(InvalidStopTarget is a PositionStatus bucket resolved BEFORE any exit is attempted - never an ExitReason; it means TradePlan.StopLoss/.TakeProfit landed on the wrong side of the REAL fill price.)");

            // ── Step 5: ExitReason breakdown, Closed positions only - the key before/after descriptive
            // number (Lot 15.3 baseline: 100% TimeHorizon, since Lot 15.3 never wired Stop/Target into
            // execution - stated here purely as a reference point, NOT a target; brief §29 forbids
            // characterizing this as an improvement or a regression). ─────────────────────────────────────
            List<SimulatedPosition> closedPositions = positions.Where(p => p.Status == PositionStatus.Closed).ToList();
            int exitStopLoss = closedPositions.Count(p => p.ExitReason == ExitReason.StopLoss);
            int exitTakeProfit = closedPositions.Count(p => p.ExitReason == ExitReason.TakeProfit);
            int exitAmbiguous = closedPositions.Count(p => p.ExitReason == ExitReason.Ambiguous);
            int exitTimeHorizon = closedPositions.Count(p => p.ExitReason == ExitReason.TimeHorizon);
            Assert.Equal(closed, exitStopLoss + exitTakeProfit + exitAmbiguous + exitTimeHorizon);

            _output.WriteLine("");
            _output.WriteLine($"=== ExitReason breakdown (Closed positions, N={closed}) ===");
            _output.WriteLine($"StopLoss={exitStopLoss} ({Pct(exitStopLoss, closed)}%), TakeProfit={exitTakeProfit} ({Pct(exitTakeProfit, closed)}%), " +
                $"Ambiguous={exitAmbiguous} ({Pct(exitAmbiguous, closed)}%), TimeHorizon={exitTimeHorizon} ({Pct(exitTimeHorizon, closed)}%)");
            _output.WriteLine("Reference point (NOT a target, NOT an improvement/regression claim, brief §29): Lot 15.3 baseline for this same regime/direction slice was 100% TimeHorizon, 0% StopLoss/TakeProfit/Ambiguous, because Lot 15.3 never wired Stop/Target into execution at all.");

            // ── Step 6: holding-bars distribution per ExitReason ────────────────────────────────────────
            _output.WriteLine("");
            _output.WriteLine("=== Holding-bars distribution by ExitReason ===");
            foreach (ExitReason reason in new[] { ExitReason.StopLoss, ExitReason.TakeProfit, ExitReason.Ambiguous, ExitReason.TimeHorizon })
            {
                List<int> holdingBars = closedPositions.Where(p => p.ExitReason == reason && p.HoldingBars is not null)
                    .Select(p => p.HoldingBars!.Value).ToList();
                if (holdingBars.Count == 0)
                {
                    _output.WriteLine($"{reason}: N=0 (no positions with this exit reason in this dataset).");
                    continue;
                }
                _output.WriteLine($"{reason}: N={holdingBars.Count}, Mean={Math.Round(holdingBars.Average(), 3)}, Min={holdingBars.Min()}, Max={holdingBars.Max()}, Median={Median(holdingBars)}");
            }

            // ── Step 7: characterize InvalidStopTarget - is it concentrated where price moved unusually
            // far between the signal bar's reference EntryPrice and the real fill price (Open[i+1])? ──────
            List<decimal> invalidGaps = new();
            List<decimal> closedGaps = new();
            for (int k = 0; k < indices.Count; k++)
            {
                int barIndex = indices[k];
                SimulatedPosition position = positions[k];
                decimal? referencePrice = result.SignalResult.Bars[barIndex].TradePlan?.EntryPrice;
                if (referencePrice is null || position.EntryPrice is null) continue;
                decimal gap = Math.Abs(position.EntryPrice.Value - referencePrice.Value);
                if (position.Status == PositionStatus.InvalidStopTarget) invalidGaps.Add(gap);
                else if (position.Status == PositionStatus.Closed) closedGaps.Add(gap);
            }

            string invalidGapObservation;
            if (invalidGaps.Count > 0 && closedGaps.Count > 0)
            {
                double invalidMeanGap = (double)invalidGaps.Average();
                double closedMeanGap = (double)closedGaps.Average();
                invalidGapObservation =
                    $"InvalidStopTarget positions (N={invalidGaps.Count}) had a mean |RealFill - SignalReferencePrice| gap of {invalidMeanGap:F4} price units, " +
                    $"versus {closedMeanGap:F4} for Closed positions (N={closedGaps.Count}) - " +
                    (invalidMeanGap > closedMeanGap
                        ? "consistent with InvalidStopTarget concentrating where price moved further between the signal bar and the fill bar."
                        : "NOT concentrated on larger signal-to-fill moves in this dataset - the wrong-side condition arises even with comparably small gaps.");
            }
            else
            {
                invalidGapObservation = $"InvalidStopTarget count = {invalidGaps.Count} in this dataset - too few (or none) to characterize the signal-to-fill gap.";
            }
            _output.WriteLine("");
            _output.WriteLine("=== InvalidStopTarget characterization (observation only, brief §6) ===");
            _output.WriteLine(invalidGapObservation);

            // ── Step 8: write CSV (additive - new file, never overwrites an existing Output/ CSV) ─────────
            string outputDir = ResolveOutputDirectory();
            string csvPath = Path.Combine(outputDir, "execution_realism_lot154.csv");

            var rows = new List<object?[]>
            {
                new object?[] { "DirectionalMeanRevertingBars", n, "" },
                new object?[] { "Status_Closed", closed, Pct(closed, n) },
                new object?[] { "Status_NotExecutable", notExecutable, Pct(notExecutable, n) },
                new object?[] { "Status_InvalidEntry", invalidEntry, Pct(invalidEntry, n) },
                new object?[] { "Status_InsufficientFutureData", insufficientFutureData, Pct(insufficientFutureData, n) },
                new object?[] { "Status_InvalidExit", invalidExit, Pct(invalidExit, n) },
                new object?[] { "Status_InvalidStopTarget", invalidStopTarget, Pct(invalidStopTarget, n) },
                new object?[] { "ExitReason_StopLoss", exitStopLoss, Pct(exitStopLoss, closed) },
                new object?[] { "ExitReason_TakeProfit", exitTakeProfit, Pct(exitTakeProfit, closed) },
                new object?[] { "ExitReason_Ambiguous", exitAmbiguous, Pct(exitAmbiguous, closed) },
                new object?[] { "ExitReason_TimeHorizon", exitTimeHorizon, Pct(exitTimeHorizon, closed) },
                new object?[] { "Lot153Baseline_ExitReason_TimeHorizon_Pct", 100.0, "ReferenceOnly_NotATarget" },
                new object?[] { "InvalidStopTarget_MeanSignalToFillGap", invalidGaps.Count > 0 ? Math.Round(invalidGaps.Average(), 6) : (decimal?)null, "" },
                new object?[] { "Closed_MeanSignalToFillGap", closedGaps.Count > 0 ? Math.Round(closedGaps.Average(), 6) : (decimal?)null, "" },
            };
            foreach (ExitReason reason in new[] { ExitReason.StopLoss, ExitReason.TakeProfit, ExitReason.Ambiguous, ExitReason.TimeHorizon })
            {
                List<int> holdingBars = closedPositions.Where(p => p.ExitReason == reason && p.HoldingBars is not null)
                    .Select(p => p.HoldingBars!.Value).ToList();
                rows.Add(new object?[] { $"HoldingBars_{reason}_N", holdingBars.Count, "" });
                rows.Add(new object?[] { $"HoldingBars_{reason}_Mean", holdingBars.Count > 0 ? Math.Round(holdingBars.Average(), 3) : (double?)null, "" });
                rows.Add(new object?[] { $"HoldingBars_{reason}_Min", holdingBars.Count > 0 ? holdingBars.Min() : (int?)null, "" });
                rows.Add(new object?[] { $"HoldingBars_{reason}_Max", holdingBars.Count > 0 ? holdingBars.Max() : (int?)null, "" });
                rows.Add(new object?[] { $"HoldingBars_{reason}_Median", holdingBars.Count > 0 ? Median(holdingBars) : (double?)null, "" });
            }

            WriteCsv(csvPath, new[] { "Metric", "Value", "Percent" }, rows);
            _output.WriteLine("");
            _output.WriteLine($"CSV written: {csvPath}");
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static double Pct(int count, int total) => total > 0 ? Math.Round(100.0 * count / total, 3) : 0.0;

    private static double Median(List<int> values)
    {
        if (values.Count == 0) return 0.0;
        List<int> sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
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
        exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException;
}
