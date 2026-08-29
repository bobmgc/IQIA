using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.Rules;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Risk;
using Xunit;
using IQIAIndicator.Tests.BacktestTests.Yahoo;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.17). INTEGRATION / NETWORK, OBSERVATION ONLY. Investigates WHY the Fusion
/// <c>Persistence</c> dimension shows exactly-zero variance on some days (Lot 14.16 finding, 6/40 days).
///
/// The central technique (brief §12/§13/§21 "RAW VS NORMALIZED" / "CAUSAL CHECK"): capture Persistence at
/// TWO points in the real pipeline for every bar - immediately after <see cref="EvidenceFusionEngine.Fuse"/>
/// (RAW, before any smoothing) and after <see cref="FusionStateManager.Update"/> (STABLE, what
/// MeanRevertingRule/StableRangeRule actually consume). If RAW varies while STABLE freezes, the cause is
/// FusionStateManager's EMA+hysteresis mechanism, not the underlying DFA/VarianceRatio evidence itself -
/// this is checked directly, not assumed.
/// </summary>
public sealed class PersistenceZeroVarianceActivationAuditLot1417Tests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public PersistenceZeroVarianceActivationAuditLot1417Tests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private sealed class BarObservation
    {
        public required int BarIndex { get; init; }
        public required DateTime Timestamp { get; init; }
        public required MarketState Winner { get; init; }
        public required double MeanRevertingScore { get; init; }
        public required double StableRangeScore { get; init; }
        public required FusionResult Fusion { get; init; }
        public required double PersistenceRaw { get; init; }
        public required double PersistenceStable { get; init; }
        public required double? DfaHurst { get; init; }
        public required bool DfaValid { get; init; }
        public required double? VarianceRatioValue { get; init; }
        public required bool VarianceRatioValid { get; init; }
        public required double StationarityStable { get; init; }
        public required double MeanReversionStable { get; init; }
        public required double StructuralStabilityStable { get; init; }
        public double ScoreDifference => MeanRevertingScore - StableRangeScore;
    }

    [Fact]
    public void Integration_Network_PersistenceZeroVarianceActivationAudit_Lot1417()
    {
        try
        {
            HistoricalSeries series = _yahoo.Require();
            Assert.True(series.Count > 0);

            string fingerprint = HistoricalSeriesFingerprint.Compute(series);
            const int warmupBars = 128;

            _output.WriteLine("=== DATASET IDENTITY (Lot 14.17, THIS run) ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");
            _output.WriteLine($"DatasetFingerprint={fingerprint}");

            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT14.17-FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            BacktestSignalPipelineResult signalResult = new BacktestEngine().RunSignalPipeline(scenario, warmupBars);
            Assert.Equal(0, signalResult.ExceptionCount);

            var fusionEngine = new EvidenceFusionEngine(new IFusionRule[]
            {
                new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new Engine.Fusion.Rules.RandomWalkRule()
            });
            var fusionState = new FusionStateManager();

            var observations = new List<BarObservation>(series.Count);

            foreach (BacktestSignalResult bar in signalResult.Bars)
            {
                if (bar.Status is BacktestSignalStatus.Rejected or BacktestSignalStatus.Exception) continue;
                if (bar.Regime is null || bar.Decision is null) continue;

                FusionResult rawResult = fusionEngine.Fuse(new FusionContext
                {
                    Evidence = bar.Regime, Timestamp = bar.Timestamp, Symbol = series.Symbol, TimeFrame = series.TimeFrame, EvaluationId = Guid.Empty
                });
                double persistenceRaw = Dim(rawResult, FusionDimension.Persistence).Value;

                FusionSnapshot snapshot = fusionState.Update(rawResult, bar.Timestamp);

                if (bar.Status != BacktestSignalStatus.Ready) continue;

                var ruleScores = bar.Decision.Candidates.ToDictionary(c => c.MarketState, c => c.FinalScore);

                observations.Add(new BarObservation
                {
                    BarIndex = bar.BarIndex,
                    Timestamp = bar.Timestamp,
                    Winner = bar.Decision.Winner,
                    MeanRevertingScore = ruleScores.GetValueOrDefault(MarketState.MeanReverting),
                    StableRangeScore = ruleScores.GetValueOrDefault(MarketState.StableRange),
                    Fusion = snapshot.StableResult,
                    PersistenceRaw = persistenceRaw,
                    PersistenceStable = Dim(snapshot.StableResult, FusionDimension.Persistence).Value,
                    DfaHurst = bar.Regime.Dfa?.Hurst,
                    DfaValid = bar.Regime.Dfa?.IsValid ?? false,
                    VarianceRatioValue = bar.Regime.VarianceRatio?.VarianceRatio,
                    VarianceRatioValid = bar.Regime.VarianceRatio?.IsValid ?? false,
                    StationarityStable = Dim(snapshot.StableResult, FusionDimension.Stationarity).Value,
                    MeanReversionStable = Dim(snapshot.StableResult, FusionDimension.MeanReversion).Value,
                    StructuralStabilityStable = Dim(snapshot.StableResult, FusionDimension.StructuralStability).Value
                });
            }

            _output.WriteLine($"ObservedBars(Ready)={observations.Count}");
            Assert.True(observations.All(o => o.DfaValid), "brief §7: DFA must be valid (past warmup) for every Ready bar on this dataset.");
            Assert.True(observations.All(o => o.VarianceRatioValid), "brief §7: VarianceRatio must be valid (past warmup) for every Ready bar.");

            // ══════════════════════ §12/§13 RAW VS NORMALIZED, §21 CAUSAL CHECK ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== RAW VS STABLE PERSISTENCE (global) ===");
            PrintStats("PersistenceRaw", observations.Select(o => o.PersistenceRaw));
            PrintStats("PersistenceStable", observations.Select(o => o.PersistenceStable));
            int rawChangedButStableFrozen = 0, bothChanged = 0, bothFrozen = 0;
            for (int i = 1; i < observations.Count; i++)
            {
                bool rawChanged = Math.Abs(observations[i].PersistenceRaw - observations[i - 1].PersistenceRaw) > 1e-9;
                bool stableChanged = Math.Abs(observations[i].PersistenceStable - observations[i - 1].PersistenceStable) > 1e-9;
                if (rawChanged && !stableChanged) rawChangedButStableFrozen++;
                else if (rawChanged && stableChanged) bothChanged++;
                else if (!rawChanged && !stableChanged) bothFrozen++;
            }
            _output.WriteLine($"Bar-to-bar transitions: RawChanged&StableFrozen(hysteresis absorbed it)={rawChangedButStableFrozen}, BothChanged={bothChanged}, BothFrozen(raw itself didn't move)={bothFrozen}, total={observations.Count - 1}");

            // ══════════════════════ §13/§14 ZERO-VARIANCE DAYS ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== DAILY DISTRIBUTION (Stable Persistence) + CLASSIFICATION ===");
            var zeroVarianceDays = new List<DateTime>();
            foreach (var dayGroup in observations.GroupBy(o => o.Timestamp.Date).OrderBy(g => g.Key))
            {
                List<BarObservation> dayBars = dayGroup.ToList();
                if (dayBars.Count < 50) continue;
                List<double> dayStable = dayBars.Select(o => o.PersistenceStable).OrderBy(v => v).ToList();
                List<double> dayRaw = dayBars.Select(o => o.PersistenceRaw).OrderBy(v => v).ToList();
                double stableStdDev = StdDev(dayStable);
                double rawStdDev = StdDev(dayRaw);
                string classification = stableStdDev < 1e-9 ? "ZERO_VARIANCE" : stableStdDev < 0.01 ? "LOW_VARIANCE" : stableStdDev < 0.05 ? "NORMAL_VARIANCE" : "HIGH_VARIANCE";
                if (stableStdDev < 1e-9) zeroVarianceDays.Add(dayGroup.Key);
                _output.WriteLine(
                    $"{dayGroup.Key:yyyy-MM-dd} [{classification}]: n={dayBars.Count}, StableMin={dayStable[0]:F4}, StableMax={dayStable[^1]:F4}, StableStdDev={stableStdDev:F6}, " +
                    $"RawMin={dayRaw[0]:F4}, RawMax={dayRaw[^1]:F4}, RawStdDev={rawStdDev:F6}, DominantWinner={dayBars.GroupBy(o => o.Winner).OrderByDescending(g => g.Count()).First().Key}");
            }
            _output.WriteLine($"ZERO_VARIANCE days found: {zeroVarianceDays.Count} -> [{string.Join(", ", zeroVarianceDays.Select(d => d.ToString("yyyy-MM-dd")))}]");

            // ══════════════════════ §4/§21 DETAIL PER ZERO-VARIANCE DAY ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== ZERO-VARIANCE DAY DETAIL (exact stable value, raw range that day, sample bars) ===");
            foreach (DateTime day in zeroVarianceDays)
            {
                List<BarObservation> dayBars = observations.Where(o => o.Timestamp.Date == day).ToList();
                double stableValue = dayBars[0].PersistenceStable;
                _output.WriteLine($"--- {day:yyyy-MM-dd}: StableValue frozen at EXACTLY {stableValue:F6} for all {dayBars.Count} bars ---");
                _output.WriteLine($"    Raw Persistence that day: min={dayBars.Min(o => o.PersistenceRaw):F6}, max={dayBars.Max(o => o.PersistenceRaw):F6}, distinct values={dayBars.Select(o => o.PersistenceRaw).Distinct().Count()}");
                _output.WriteLine($"    DFA Hurst that day: min={dayBars.Min(o => o.DfaHurst):F4}, max={dayBars.Max(o => o.DfaHurst):F4}");
                _output.WriteLine($"    VarianceRatio that day: min={dayBars.Min(o => o.VarianceRatioValue):F4}, max={dayBars.Max(o => o.VarianceRatioValue):F4}");
                _output.WriteLine($"    Winner distribution: [{string.Join(", ", dayBars.GroupBy(o => o.Winner).Select(g => $"{g.Key}={g.Count()}"))}]");
                _output.WriteLine($"    ScoreDifference(MR-SR): mean={dayBars.Average(o => o.ScoreDifference):F4}");
                // First 3 and last 3 bars of the day, to see the transition shape (brief §6)
                foreach (BarObservation o in dayBars.Take(3).Concat(dayBars.Skip(Math.Max(0, dayBars.Count - 3))))
                    _output.WriteLine($"      {o.Timestamp:O}: Raw={o.PersistenceRaw:F6}, Stable={o.PersistenceStable:F6}, Hurst={o.DfaHurst:F4}, VR={o.VarianceRatioValue:F4}, Winner={o.Winner}");
            }

            // ══════════════════════ §5 ZERO-VARIANCE VS NORMAL DAYS COMPARISON ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== ZERO-VARIANCE DAYS vs ALL OTHER DAYS: mean dimension values ===");
            List<BarObservation> zeroVarBars = observations.Where(o => zeroVarianceDays.Contains(o.Timestamp.Date)).ToList();
            List<BarObservation> otherBars = observations.Where(o => !zeroVarianceDays.Contains(o.Timestamp.Date)).ToList();
            _output.WriteLine($"ZeroVarianceDays (n={zeroVarBars.Count}): meanStationarity={zeroVarBars.Average(o => o.StationarityStable):F4}, meanMeanReversion={zeroVarBars.Average(o => o.MeanReversionStable):F4}, meanStructuralStability={zeroVarBars.Average(o => o.StructuralStabilityStable):F4}, meanScoreDiff={zeroVarBars.Average(o => o.ScoreDifference):F4}");
            _output.WriteLine($"OtherDays        (n={otherBars.Count}): meanStationarity={otherBars.Average(o => o.StationarityStable):F4}, meanMeanReversion={otherBars.Average(o => o.MeanReversionStable):F4}, meanStructuralStability={otherBars.Average(o => o.StructuralStabilityStable):F4}, meanScoreDiff={otherBars.Average(o => o.ScoreDifference):F4}");
            _output.WriteLine("Regime distribution, ZeroVarianceDays: " + string.Join(", ", zeroVarBars.GroupBy(o => o.Winner).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}={100.0 * g.Count() / zeroVarBars.Count:F1}%")));
            _output.WriteLine("Regime distribution, OtherDays:        " + string.Join(", ", otherBars.GroupBy(o => o.Winner).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}={100.0 * g.Count() / otherBars.Count:F1}%")));

            // ══════════════════════ §16/§17 REGIME DISTRIBUTION + ZERO-VARIANCE × REGIME ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== PERSISTENCE(STABLE) VARIANCE BY REGIME ===");
            foreach (var g in observations.GroupBy(o => o.Winner).OrderByDescending(g => g.Count()))
                _output.WriteLine($"Winner={g.Key} (n={g.Count()}): stdDev={StdDev(g.Select(o => o.PersistenceStable).ToList()):F4}, median={Percentile(g.Select(o => o.PersistenceStable).OrderBy(v => v).ToList(), 0.5):F4}, range=[{g.Min(o => o.PersistenceStable):F4},{g.Max(o => o.PersistenceStable):F4}]");

            // ══════════════════════ §6/§20 CHANGE-POINT / FROZEN-RUN ANALYSIS ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== FROZEN-RUN ANALYSIS (longest consecutive-bar stretches of bit-identical StablePersistence) ===");
            var runs = new List<(double Value, int Length, DateTime Start, DateTime End)>();
            int runStart = 0;
            for (int i = 1; i <= observations.Count; i++)
            {
                bool sameAsPrevious = i < observations.Count && Math.Abs(observations[i].PersistenceStable - observations[runStart].PersistenceStable) < 1e-9;
                if (!sameAsPrevious)
                {
                    runs.Add((observations[runStart].PersistenceStable, i - runStart, observations[runStart].Timestamp, observations[i - 1].Timestamp));
                    runStart = i;
                }
            }
            foreach (var run in runs.OrderByDescending(r => r.Length).Take(10))
                _output.WriteLine($"Value={run.Value:F6}, Length={run.Length} bars (~{run.Length * 5 / 60.0:F1}h), {run.Start:yyyy-MM-dd HH:mm} .. {run.End:yyyy-MM-dd HH:mm}");
            _output.WriteLine($"Total distinct runs={runs.Count}, runs of length>=20 bars={runs.Count(r => r.Length >= 20)}");

            double maxJump = 0; DateTime maxJumpTime = default;
            for (int i = 1; i < observations.Count; i++)
            {
                double jump = Math.Abs(observations[i].PersistenceStable - observations[i - 1].PersistenceStable);
                if (jump > maxJump) { maxJump = jump; maxJumpTime = observations[i].Timestamp; }
            }
            _output.WriteLine($"Largest single bar-to-bar jump in StablePersistence: {maxJump:F4} at {maxJumpTime:O}");

            // ══════════════════════ §18/§19 ACTIVATION ZONE / THRESHOLD LOCALIZATION (fine bins) ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== FINE-GRAINED ACTIVATION ZONE (30 equal-count bins of StablePersistence vs ScoreDifference) ===");
            List<BarObservation> sortedByPersistence = observations.OrderBy(o => o.PersistenceStable).ToList();
            int binSize = sortedByPersistence.Count / 30;
            for (int b = 0; b < 30; b++)
            {
                List<BarObservation> bin = sortedByPersistence.Skip(b * binSize).Take(b == 29 ? sortedByPersistence.Count - b * binSize : binSize).ToList();
                double lo = bin.Min(o => o.PersistenceStable), hi = bin.Max(o => o.PersistenceStable);
                double meanDiff = bin.Average(o => o.ScoreDifference);
                double srWinRate = bin.Count(o => o.Winner == MarketState.StableRange) / (double)bin.Count;
                _output.WriteLine($"Bin {b:D2} [{lo:F4},{hi:F4}] n={bin.Count}: meanScoreDiff={meanDiff:F4}, SR winRate={srWinRate:P1}");
            }

            // ══════════════════════ §25 ABLATION CONTROL ══════════════════════
            _output.WriteLine("");
            _output.WriteLine("=== ABLATION CONTROL (reproduction check only, Lot 14.15/14.16 reference: 0.9652-0.9653 / ~0.108 / 0.9913) ===");
            double meanStationarity = observations.Average(o => o.StationarityStable);
            double meanMeanReversion = observations.Average(o => o.MeanReversionStable);
            double meanPersistence = observations.Average(o => o.PersistenceStable);
            RunAblation("Baseline", observations, f => f);
            RunAblation("Shared dimensions removed", observations, f => Clamp(Clamp(f, FusionDimension.Stationarity, meanStationarity), FusionDimension.MeanReversion, meanMeanReversion));
            RunAblation("Persistence removed", observations, f => Clamp(f, FusionDimension.Persistence, meanPersistence));
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void RunAblation(string label, List<BarObservation> observations, Func<FusionResult, FusionResult> transform)
    {
        var mrRule = new MeanRevertingRule();
        var srRule = new StableRangeRule();
        var mr = new List<double>(observations.Count);
        var sr = new List<double>(observations.Count);
        foreach (BarObservation o in observations)
        {
            FusionResult modified = transform(o.Fusion);
            var mrBuilder = new DecisionResultBuilder();
            mrRule.Evaluate(new DecisionContext { FusionResult = modified, Evidence = null! }, mrBuilder);
            var srBuilder = new DecisionResultBuilder();
            srRule.Evaluate(new DecisionContext { FusionResult = modified, Evidence = null! }, srBuilder);
            mr.Add(mrBuilder.Confidence);
            sr.Add(srBuilder.Confidence);
        }
        _output.WriteLine($"{label}: Corr(MR,SR)={Correlation(mr, sr):F4}");
    }

    private static FusionResult Clamp(FusionResult source, FusionDimension dimension, double value)
    {
        var builder = new FusionResultBuilder();
        foreach (var kv in source.Dimensions)
            builder.Dimensions[kv.Key] = kv.Value;
        FusionConfidence original = Dim(source, dimension);
        builder.Dimensions[dimension] = original with { Value = value };
        return builder.Build();
    }

    private static FusionConfidence Dim(FusionResult result, FusionDimension dimension) =>
        result.Dimensions.TryGetValue(dimension, out FusionConfidence? confidence)
            ? confidence
            : new FusionConfidence { Value = 0.0, Confidence = 0.0, Explanation = "Absent", IsAvailable = false };

    private void PrintStats(string label, IEnumerable<double> values)
    {
        List<double> sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) { _output.WriteLine($"{label}: n=0"); return; }
        _output.WriteLine(
            $"{label}: n={sorted.Count}, min={sorted[0]:F4}, max={sorted[^1]:F4}, mean={sorted.Average():F4}, stdDev={StdDev(sorted):F4}, " +
            $"P25={Percentile(sorted, 0.25):F4}, P50={Percentile(sorted, 0.50):F4}, P75={Percentile(sorted, 0.75):F4}");
    }

    private static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0.0;
        double mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 1) return sorted[0];
        double rank = p * (sorted.Count - 1);
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sorted[lower];
        double fraction = rank - lower;
        return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
    }

    private static double Correlation(IEnumerable<double> xs, IEnumerable<double> ys)
    {
        List<double> x = xs.ToList();
        List<double> y = ys.ToList();
        if (x.Count != y.Count || x.Count < 2) return double.NaN;
        double meanX = x.Average(), meanY = y.Average();
        double cov = 0.0, varX = 0.0, varY = 0.0;
        for (int i = 0; i < x.Count; i++)
        {
            double dx = x[i] - meanX, dy = y[i] - meanY;
            cov += dx * dy; varX += dx * dx; varY += dy * dy;
        }
        if (varX <= 0.0 || varY <= 0.0) return double.NaN;
        return cov / Math.Sqrt(varX * varY);
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException;
}
