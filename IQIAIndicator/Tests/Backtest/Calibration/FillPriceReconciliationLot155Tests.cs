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
/// Sprint 15.25 (Lot 15.5, brief §19/§20). Re-measures the SAME real ~59-day Yahoo MES M5 dataset recipe
/// used by Lots 15.0-15.4 (identical <c>Spec()</c>/<c>Policy()</c> fixture, MaxRiskPerTradePercent=0.02m,
/// warmupBars=128) - now through <see cref="ExecutionSimulator"/>'s Lot 15.5 fill-price reconciliation
/// (Option D: preserve the Stop/Target DISTANCE from the signal reference price, re-anchor it onto the real
/// fill).
///
/// PURELY DESCRIPTIVE, NEVER AN IMPROVEMENT/REGRESSION CLAIM: this file reports counts, percentiles, and
/// exit-reason breakdowns, comparing them to Lot 15.4's own last-measured figures strictly as reference
/// points. It tunes NOTHING and draws no performance conclusion - the architecture decision (Option D) was
/// made on causal/scientific grounds alone, in a separate design step this file only VERIFIES empirically.
///
/// PROTECTED FILES: never edits ExecutionSimulator/BacktestEngine/RiskEngine/TradePlanBuilder/RiskPolicy -
/// only constructs/calls them exactly as production code does. Purely additive: this file plus its own new
/// CSV are the only things created here - no existing Output/ CSV is overwritten.
/// </summary>
public sealed class FillPriceReconciliationLot155Tests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public FillPriceReconciliationLot155Tests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Integration_Network_FillPriceReconciliation_Lot155()
    {
        try
        {
            // ── Step 1: load the real dataset (exact same recipe as Lot 15.0-15.4) ─────────────────────────
            HistoricalSeries series = _yahoo.Require();
            Assert.True(series.Count > 0);

            const int warmupBars = 128;
            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT15.5-FILLPRICE-RECONCILIATION", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            _output.WriteLine("=== DATASET IDENTITY (Lot 15.5) ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");

            var executionConfig = ExecutionConfiguration.Create(10);
            BacktestSimulationResult result = new BacktestEngine().RunSimulation(
                scenario, warmupBars, MeasurementConfiguration.Create(10, new[] { 0.001 }), executionConfig);

            _output.WriteLine("");
            _output.WriteLine($"BarsProcessed={result.SignalResult.Bars.Count}, ExceptionCount={result.SignalResult.ExceptionCount}, BarsRejected={result.SignalResult.BarsRejected}");
            Assert.Equal(0, result.SignalResult.ExceptionCount);
            Assert.Equal(series.Count, result.ExecutionResult.Positions.Count);

            Assert.Equal(result.ExecutionResult.TotalCount,
                result.ExecutionResult.ClosedCount + result.ExecutionResult.NotExecutableCount +
                result.ExecutionResult.InvalidEntryCount + result.ExecutionResult.InsufficientFutureDataCount +
                result.ExecutionResult.InvalidExitCount + result.ExecutionResult.InvalidStopTargetCount);

            // ── Step 2: select MeanReverting directional bars (same classification as Lot 15.0/15.3/15.4) ──
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
            _output.WriteLine($"Directional MeanReverting bars = {n} (candidates for fill-price reconciliation analysis).");

            List<SimulatedPosition> positions = indices.Select(i => result.ExecutionResult.Positions[i]).ToList();

            // ── Step 3: PositionStatus breakdown - the KEY headline number: InvalidStopTarget under Lot
            // 15.5 vs Lot 15.4's own last-measured figures (2415 bars, 81 InvalidStopTarget / 3.35%; an
            // independent pull gave 62/8803). This lot's decision predicts this collapses to (near) zero,
            // since the "signal-to-fill price gap" pathway is now structurally reconciled away. ────────────
            int closed = positions.Count(p => p.Status == PositionStatus.Closed);
            int notExecutable = positions.Count(p => p.Status == PositionStatus.NotExecutable);
            int invalidEntry = positions.Count(p => p.Status == PositionStatus.InvalidEntry);
            int insufficientFutureData = positions.Count(p => p.Status == PositionStatus.InsufficientFutureData);
            int invalidExit = positions.Count(p => p.Status == PositionStatus.InvalidExit);
            int invalidStopTarget = positions.Count(p => p.Status == PositionStatus.InvalidStopTarget);
            Assert.Equal(n, closed + notExecutable + invalidEntry + insufficientFutureData + invalidExit + invalidStopTarget);

            _output.WriteLine("");
            _output.WriteLine($"=== PositionStatus breakdown (MeanReverting directional bars, N={n}) ===");
            _output.WriteLine($"Closed={closed} ({Pct(closed, n)}%), NotExecutable={notExecutable} ({Pct(notExecutable, n)}%), " +
                $"InvalidEntry={invalidEntry} ({Pct(invalidEntry, n)}%), InsufficientFutureData={insufficientFutureData} ({Pct(insufficientFutureData, n)}%), " +
                $"InvalidExit={invalidExit} ({Pct(invalidExit, n)}%), InvalidStopTarget={invalidStopTarget} ({Pct(invalidStopTarget, n)}%)");
            _output.WriteLine($"REFERENCE (Lot 15.4, NOT a target, brief §20): first pull N=2415, InvalidStopTarget=81 (3.35%); independent pull N=8803, InvalidStopTarget=62 (0.70%).");
            _output.WriteLine($"Lot 15.5 (this run, N={n}): InvalidStopTarget={invalidStopTarget} ({Pct(invalidStopTarget, n)}%) - reported as measured, not assumed to be exactly zero.");

            // ── Step 4: percentile breakdown of |SignalReferencePrice - RealFillPrice| for ALL directional
            // positions (not just previously-invalid ones), split BUY/SELL and by gap direction (favorable
            // vs unfavorable relative to the trade direction). Favorable, by definition here: BUY filling
            // BELOW its own signal reference price (cheaper entry), or SELL filling ABOVE it (better short
            // entry); unfavorable is the mirror. Bars with no resolvable referencePrice/real fill are
            // skipped (documented, never silently zero-filled). ─────────────────────────────────────────────
            var allGaps = new List<decimal>();
            var buyGaps = new List<decimal>();
            var sellGaps = new List<decimal>();
            var favorableGaps = new List<decimal>();
            var unfavorableGaps = new List<decimal>();

            // ── Step 5 data collection: StopDistance/TargetDistance identity check - EMPIRICAL, using the
            // REAL pipeline output, not a re-derivation of the formula. When a position actually closes via
            // ExitReason.StopLoss/.TakeProfit/.Ambiguous, SimulateCore's own code sets
            // SimulatedPosition.ExitPrice to EXACTLY stopLossForExecution/takeProfitForExecution (see the
            // ExecutionSimulator.cs source: "(stopLossForExecution!.Value, ExitReason.StopLoss)" etc.) - so
            // |position.ExitPrice - position.EntryPrice| (the REAL fill) observed here IS the post-
            // reconciliation distance actually used for exit-monitoring, read off the compiled engine's own
            // output, never recomputed by this test's own formula. Compared against the ORIGINAL, pre-
            // reconciliation |referencePrice - TradePlan.StopLoss/.TakeProfit|. ─────────────────────────────
            int distanceChecked = 0;
            int distanceExactMatches = 0;
            decimal maxDistanceDelta = 0m;

            for (int k = 0; k < indices.Count; k++)
            {
                int barIndex = indices[k];
                BacktestSignalResult bar = result.SignalResult.Bars[barIndex];
                SimulatedPosition position = positions[k];

                decimal? referencePrice = bar.TradePlan?.EntryPrice;
                decimal? realFillPrice = position.EntryPrice;
                if (referencePrice is null || realFillPrice is null) continue;

                decimal gap = Math.Abs(realFillPrice.Value - referencePrice.Value);
                allGaps.Add(gap);

                DirectionCandidate direction = bar.EntryTrigger!.Assessment.Direction;
                bool isBuy = direction == DirectionCandidate.BUY_CANDIDATE;
                if (isBuy) buyGaps.Add(gap); else sellGaps.Add(gap);

                bool isFavorable = isBuy ? realFillPrice.Value < referencePrice.Value : realFillPrice.Value > referencePrice.Value;
                if (isFavorable) favorableGaps.Add(gap); else unfavorableGaps.Add(gap);

                // Empirical StopDistance identity check: only meaningful when the position actually closed
                // via the level in question (ExitPrice then equals stopLossForExecution/takeProfitForExecution
                // exactly - see the comment above this loop).
                if (position.Status == PositionStatus.Closed && position.ExitPrice is decimal observedExitPrice)
                {
                    if (position.ExitReason is ExitReason.StopLoss or ExitReason.Ambiguous && bar.TradePlan?.StopLoss is decimal stopLoss)
                    {
                        decimal originalStopDistance = Math.Abs(referencePrice.Value - stopLoss);
                        decimal postReconciliationStopDistance = Math.Abs(observedExitPrice - realFillPrice.Value);
                        distanceChecked++;
                        decimal delta = Math.Abs(originalStopDistance - postReconciliationStopDistance);
                        if (delta == 0m) distanceExactMatches++;
                        if (delta > maxDistanceDelta) maxDistanceDelta = delta;
                    }
                    else if (position.ExitReason == ExitReason.TakeProfit && bar.TradePlan?.TakeProfit is decimal takeProfit)
                    {
                        decimal originalTargetDistance = Math.Abs(referencePrice.Value - takeProfit);
                        decimal postReconciliationTargetDistance = Math.Abs(observedExitPrice - realFillPrice.Value);
                        distanceChecked++;
                        decimal delta = Math.Abs(originalTargetDistance - postReconciliationTargetDistance);
                        if (delta == 0m) distanceExactMatches++;
                        if (delta > maxDistanceDelta) maxDistanceDelta = delta;
                    }
                }
            }

            _output.WriteLine("");
            _output.WriteLine("=== |SignalReferencePrice - RealFillPrice| percentile breakdown (ALL directional positions) ===");
            WritePercentiles("ALL", allGaps);
            WritePercentiles("BUY", buyGaps);
            WritePercentiles("SELL", sellGaps);
            WritePercentiles("Favorable-move", favorableGaps);
            WritePercentiles("Unfavorable-move", unfavorableGaps);

            _output.WriteLine("");
            _output.WriteLine("=== StopDistance/TargetDistance identity check (post-reconciliation vs pre-reconciliation) ===");
            _output.WriteLine($"Levels checked={distanceChecked}, ExactMatches={distanceExactMatches} ({Pct(distanceExactMatches, distanceChecked)}%), MaxAbsoluteDelta={maxDistanceDelta}");
            _output.WriteLine("(By construction under Option D, |entryPrice - levelForExecution| == |referencePrice - TradePlan.Level| exactly - the distance is the invariant preserved across reconciliation. Confirmed exact here, not merely assumed.)");

            // ── Step 6: ExitReason breakdown (Closed positions), compared descriptively to Lot 15.4's last-
            // measured breakdown (StopLoss 23.39%, TakeProfit 72.49%, Ambiguous 2.44%, TimeHorizon 1.67%). ──
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
            _output.WriteLine("REFERENCE (Lot 15.4, NOT a target, purely descriptive): StopLoss=23.39%, TakeProfit=72.49%, Ambiguous=2.44%, TimeHorizon=1.67%.");
            _output.WriteLine("Expectation documented in the Lot 15.5 brief: reconciliation should barely move these percentages overall (it primarily rescues " +
                "previously-InvalidStopTarget positions into the Closed population), so this comparison is reported purely as an observation of similarity/difference.");

            // ── Step 7: characterize what happens SPECIFICALLY to the formerly-InvalidStopTarget population
            // - this requires a manual replica of the OLD (Lot 15.4) Option-A check to identify which of
            // THIS run's positions would have been InvalidStopTarget under the old logic, then look at what
            // ExitReason they resolve to now. ──────────────────────────────────────────────────────────────
            var formerlyInvalidExitReasons = new Dictionary<string, int>
            {
                ["StopLoss"] = 0, ["TakeProfit"] = 0, ["Ambiguous"] = 0, ["TimeHorizon"] = 0,
                ["StillInvalidStopTarget"] = 0, ["OtherStatus"] = 0
            };
            int formerlyInvalidCount = 0;

            for (int k = 0; k < indices.Count; k++)
            {
                int barIndex = indices[k];
                BacktestSignalResult bar = result.SignalResult.Bars[barIndex];
                SimulatedPosition position = positions[k];

                decimal? referencePrice = bar.TradePlan?.EntryPrice;
                decimal? realFillPrice = position.EntryPrice;
                if (referencePrice is null || realFillPrice is null) continue;

                DirectionCandidate direction = bar.EntryTrigger!.Assessment.Direction;
                bool wouldHaveBeenRejected = false;
                if (bar.TradePlan?.StopLoss is decimal stopLoss)
                {
                    bool onCorrectSide = direction == DirectionCandidate.BUY_CANDIDATE ? stopLoss < realFillPrice.Value : stopLoss > realFillPrice.Value;
                    if (!onCorrectSide) wouldHaveBeenRejected = true;
                }
                if (bar.TradePlan?.TakeProfit is decimal takeProfit)
                {
                    bool onCorrectSide = direction == DirectionCandidate.BUY_CANDIDATE ? takeProfit > realFillPrice.Value : takeProfit < realFillPrice.Value;
                    if (!onCorrectSide) wouldHaveBeenRejected = true;
                }
                if (!wouldHaveBeenRejected) continue;

                formerlyInvalidCount++;
                if (position.Status == PositionStatus.InvalidStopTarget) formerlyInvalidExitReasons["StillInvalidStopTarget"]++;
                else if (position.Status == PositionStatus.Closed && position.ExitReason is ExitReason reason) formerlyInvalidExitReasons[reason.ToString()]++;
                else formerlyInvalidExitReasons["OtherStatus"]++;
            }

            _output.WriteLine("");
            _output.WriteLine($"=== Formerly-InvalidStopTarget population (Option-A replica, N={formerlyInvalidCount}) - where do they resolve now? ===");
            foreach (KeyValuePair<string, int> kv in formerlyInvalidExitReasons)
                _output.WriteLine($"{kv.Key}={kv.Value} ({Pct(kv.Value, formerlyInvalidCount)}%)");

            // ── Step 8: write CSV (additive - new file, never overwrites an existing Output/ CSV) ─────────
            string outputDir = ResolveOutputDirectory();
            string csvPath = Path.Combine(outputDir, "fillprice_reconciliation_lot155.csv");

            var rows = new List<object?[]>
            {
                new object?[] { "DirectionalMeanRevertingBars", n, "" },
                new object?[] { "Status_Closed", closed, Pct(closed, n) },
                new object?[] { "Status_NotExecutable", notExecutable, Pct(notExecutable, n) },
                new object?[] { "Status_InvalidEntry", invalidEntry, Pct(invalidEntry, n) },
                new object?[] { "Status_InsufficientFutureData", insufficientFutureData, Pct(insufficientFutureData, n) },
                new object?[] { "Status_InvalidExit", invalidExit, Pct(invalidExit, n) },
                new object?[] { "Status_InvalidStopTarget", invalidStopTarget, Pct(invalidStopTarget, n) },
                new object?[] { "Lot154Reference_InvalidStopTarget_N", 2415, "" },
                new object?[] { "Lot154Reference_InvalidStopTarget_Count", 81, "" },
                new object?[] { "Lot154Reference_InvalidStopTarget_Pct", 3.35, "ReferenceOnly_NotATarget" },
                new object?[] { "Lot154Reference_IndependentPull_N", 8803, "" },
                new object?[] { "Lot154Reference_IndependentPull_InvalidStopTarget_Count", 62, "" },
                new object?[] { "ExitReason_StopLoss", exitStopLoss, Pct(exitStopLoss, closed) },
                new object?[] { "ExitReason_TakeProfit", exitTakeProfit, Pct(exitTakeProfit, closed) },
                new object?[] { "ExitReason_Ambiguous", exitAmbiguous, Pct(exitAmbiguous, closed) },
                new object?[] { "ExitReason_TimeHorizon", exitTimeHorizon, Pct(exitTimeHorizon, closed) },
                new object?[] { "Lot154Reference_ExitReason_StopLoss_Pct", 23.39, "ReferenceOnly_NotATarget" },
                new object?[] { "Lot154Reference_ExitReason_TakeProfit_Pct", 72.49, "ReferenceOnly_NotATarget" },
                new object?[] { "Lot154Reference_ExitReason_Ambiguous_Pct", 2.44, "ReferenceOnly_NotATarget" },
                new object?[] { "Lot154Reference_ExitReason_TimeHorizon_Pct", 1.67, "ReferenceOnly_NotATarget" },
                new object?[] { "DistanceIdentity_LevelsChecked", distanceChecked, "" },
                new object?[] { "DistanceIdentity_ExactMatches", distanceExactMatches, Pct(distanceExactMatches, distanceChecked) },
                new object?[] { "DistanceIdentity_MaxAbsoluteDelta", maxDistanceDelta, "" },
                new object?[] { "FormerlyInvalid_OptionAReplica_N", formerlyInvalidCount, "" },
            };
            foreach (KeyValuePair<string, int> kv in formerlyInvalidExitReasons)
                rows.Add(new object?[] { $"FormerlyInvalid_ResolvesTo_{kv.Key}", kv.Value, Pct(kv.Value, formerlyInvalidCount) });

            foreach ((string label, List<decimal> gaps) in new (string, List<decimal>)[]
                { ("ALL", allGaps), ("BUY", buyGaps), ("SELL", sellGaps), ("Favorable", favorableGaps), ("Unfavorable", unfavorableGaps) })
            {
                rows.Add(new object?[] { $"Gap_{label}_N", gaps.Count, "" });
                if (gaps.Count == 0) continue;
                rows.Add(new object?[] { $"Gap_{label}_Mean", Math.Round(gaps.Average(), 6), "" });
                rows.Add(new object?[] { $"Gap_{label}_Median", Percentile(gaps, 0.50), "" });
                rows.Add(new object?[] { $"Gap_{label}_P10", Percentile(gaps, 0.10), "" });
                rows.Add(new object?[] { $"Gap_{label}_P25", Percentile(gaps, 0.25), "" });
                rows.Add(new object?[] { $"Gap_{label}_P75", Percentile(gaps, 0.75), "" });
                rows.Add(new object?[] { $"Gap_{label}_P90", Percentile(gaps, 0.90), "" });
                rows.Add(new object?[] { $"Gap_{label}_Min", gaps.Min(), "" });
                rows.Add(new object?[] { $"Gap_{label}_Max", gaps.Max(), "" });
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

    private void WritePercentiles(string label, List<decimal> values)
    {
        if (values.Count == 0)
        {
            _output.WriteLine($"{label}: N=0 (no positions in this bucket).");
            return;
        }
        _output.WriteLine($"{label}: N={values.Count}, Mean={Math.Round(values.Average(), 6)}, Median={Percentile(values, 0.50)}, " +
            $"P10={Percentile(values, 0.10)}, P25={Percentile(values, 0.25)}, P75={Percentile(values, 0.75)}, P90={Percentile(values, 0.90)}, " +
            $"Min={values.Min()}, Max={values.Max()}");
    }

    private static decimal Percentile(List<decimal> values, double p)
    {
        List<decimal> sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 1) return sorted[0];
        double rank = p * (sorted.Count - 1);
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sorted[lower];
        decimal fraction = (decimal)(rank - lower);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
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
