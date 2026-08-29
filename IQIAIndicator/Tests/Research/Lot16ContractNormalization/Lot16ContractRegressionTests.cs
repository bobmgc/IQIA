using System;
using System.Collections.Generic;
using System.Globalization;
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
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Risk;
using Xunit;
using DecisionRules = IQIAIndicator.Engine.Decision.Rules;
using FusionEngine = IQIAIndicator.Engine.Fusion.EvidenceFusionEngine;
using IQIAIndicator.Tests.BacktestTests.Yahoo;

namespace IQIAIndicator.Tests.Research.Lot16ContractNormalization;

/// <summary>
/// LOT 16 - "Fusion Dimension Contract Repair &amp; Scientific Normalization". Integration checks for the
/// revised <see cref="FusionDimension.StructuralBreak"/> Strength contract (<c>r / (1 + r)</c>,
/// <c>r = peakCusum / threshold</c>) on the real Yahoo MES M5 dataset. Network-guarded: a connectivity
/// failure logs SKIPPED and does not fail the suite.
///
/// Mandatory Lot 16 test items covered here:
///   1. Bornes           - every bar's Strength in [0, 1)
///   2. Monotonicite     - ordering by raw CUSUM ratio never decreases Strength (on the real sample)
///   3. Non saturation   - among Detected==true bars: Variance(Strength) &gt; 0, saturation rate ~ 0
///   4. Determinisme     - two reconstructions from the same pipeline result -&gt; identical SHA-256
///   6. Run isolation    - a second independent BacktestEngine run -&gt; identical Strength sequence
///   7. 5-dim regression - the 4 evidence dimensions other than StructuralBreak, and the Decision Winner,
///                         are bit-identical with vs without StructuralBreakEvidenceRule
///   8. Dataset identity - range / bar count / fingerprint recorded
///
/// (Look-ahead for the Strength contract is already covered by
/// <c>Tests/Backtest/Pipeline/StructuralBreakLookAheadTests</c> - a pure per-bar function of that bar's
/// own causal CusumResult - and continues to pass unchanged under Lot 16.)
/// </summary>
public sealed class Lot16ContractRegressionTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public Lot16ContractRegressionTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static readonly FusionDimension[] OtherDims =
    {
        FusionDimension.Stationarity, FusionDimension.Persistence, FusionDimension.MeanReversion, FusionDimension.RandomWalk
    };

    private static FusionEngine ProductionFusion() => new(new IFusionRule[]
    {
        new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new RandomWalkRule(),
        new StructuralBreakEvidenceRule()
    });

    private static FusionEngine ReferenceFusionWithoutStructuralBreak() => new(new IFusionRule[]
    {
        new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new RandomWalkRule()
    });

    private static DecisionEngine ReferenceDecision() => new(new DecisionRules.IDecisionRule[]
    {
        new DecisionRules.StableRangeRule(), new DecisionRules.TrendingRule(), new DecisionRules.MeanRevertingRule(),
        new DecisionRules.StructuralBreakRule(), new DecisionRules.RandomWalkRule()
    });

    private sealed record BarRow(
        int BarIndex, bool Detected, double RawRatio, double Strength,
        double[] OtherRaw, MarketState ProductionWinner, MarketState ReferenceWinner);

    [Fact]
    public void Integration_Network_Lot16_StrengthContract_RepairedAndRegressionClean()
    {
        try
        {
            HistoricalSeries series = _yahoo.Require();
            Assert.True(series.Count > 0);

            string fingerprint = HistoricalSeriesFingerprint.Compute(series);
            const int warmupBars = 128;

            _output.WriteLine("=== LOT 16 DATASET IDENTITY (mandatory item 8) ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");
            _output.WriteLine($"Fingerprint={fingerprint}");

            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT16-REG", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            BacktestSignalPipelineResult run1 = new BacktestEngine().RunSignalPipeline(scenario, warmupBars);
            BacktestSignalPipelineResult run2 = new BacktestEngine().RunSignalPipeline(scenario, warmupBars);
            Assert.Equal(0, run1.ExceptionCount);
            Assert.Equal(0, run2.ExceptionCount);

            List<BarRow> rows = BuildRows(run1, series);
            List<BarRow> rowsRerun = BuildRows(run2, series);
            _output.WriteLine($"ReadyBars={run1.ReadyBars}, Rows={rows.Count}, Detected={rows.Count(r => r.Detected)}");
            Assert.True(rows.Count > 1000);

            // ── item 1: bounds ────────────────────────────────────────────────────────────────────────
            Assert.All(rows, r => Assert.InRange(r.Strength, 0.0, 1.0));
            Assert.All(rows, r => Assert.True(r.Strength < 1.0, $"Bar {r.BarIndex}: Strength {r.Strength} reached 1.0"));

            // ── item 2: monotonicity on the real sample ──────────────────────────────────────────────
            var orderedByRatio = rows.OrderBy(r => r.RawRatio).ToList();
            int monoViolations = 0;
            for (int i = 1; i < orderedByRatio.Count; i++)
                if (orderedByRatio[i].Strength < orderedByRatio[i - 1].Strength - 1e-12) monoViolations++;
            _output.WriteLine($"Monotonicity violations (Strength decreasing as raw ratio increases): {monoViolations}");
            Assert.Equal(0, monoViolations);

            // ── item 3: non-saturation among detected bars ───────────────────────────────────────────
            List<double> detStrength = rows.Where(r => r.Detected).Select(r => r.Strength).ToList();
            Assert.True(detStrength.Count > 500);
            double detMean = detStrength.Average();
            double detVar = detStrength.Sum(s => (s - detMean) * (s - detMean)) / (detStrength.Count - 1);
            int detDistinct = detStrength.Select(s => Math.Round(s, 9)).Distinct().Count();
            int detSaturated = detStrength.Count(s => s >= 0.9999);
            _output.WriteLine($"[DETECTED] N={detStrength.Count} Var={detVar:G6} Distinct={detDistinct} " +
                              $"Saturated(>=0.9999)={detSaturated} ({100.0 * detSaturated / detStrength.Count:F3}%) " +
                              $"Min={detStrength.Min():F6} Median={Median(detStrength):F6} Max={detStrength.Max():F6}");
            Assert.True(detVar > 0.0, "Lot 16 core outcome: Strength variance among detected bars must be > 0 (was exactly 0).");
            Assert.True(detDistinct > 100, $"Expected a genuinely continuous Strength among detected bars, got {detDistinct} distinct values.");
            Assert.True(detSaturated < detStrength.Count / 20, "Strength must no longer saturate at ~1.0 for the vast majority of detected bars.");

            // ── item 4: determinism ─────────────────────────────────────────────────────────────────
            string hash1 = HashStrengths(rows);
            string hash1b = HashStrengths(BuildRows(run1, series));
            _output.WriteLine($"[DETERMINISM] hash(rows)={hash1}  hash(rebuild)={hash1b}  identical={hash1 == hash1b}");
            Assert.Equal(hash1, hash1b);

            // ── item 6: run isolation (independent BacktestEngine instance, same scenario) ────────────
            string hash2 = HashStrengths(rowsRerun);
            _output.WriteLine($"[RUN ISOLATION] hash(run1)={hash1}  hash(run2)={hash2}  identical={hash1 == hash2}");
            Assert.Equal(hash1, hash2);

            // ── item 7: the other 5 dimensions + Decision Winner are untouched ───────────────────────
            // The 4 non-StructuralBreak evidence dimensions are compared bit-for-bit between the
            // production (5-rule) and reference (4-rule) raw fusion inside BuildRows - any difference
            // throws there. StructuralStability is produced only by FusionStateManager (not an IFusionRule)
            // and is likewise unaffected. Here we only need to confirm the arbitrated regime is stable.
            int winnerMismatches = rows.Count(r => r.ProductionWinner != r.ReferenceWinner);
            _output.WriteLine($"[REGRESSION] otherDimsHash(reference-engine 4 dims)={HashOtherDims(rows)}");
            _output.WriteLine($"[REGRESSION] Decision.Winner mismatches (production vs 5-rule reference DecisionEngine) = {winnerMismatches}");
            Assert.Equal(0, winnerMismatches);
        }
        catch (Exception exception) when (exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private List<BarRow> BuildRows(BacktestSignalPipelineResult result, HistoricalSeries series)
    {
        FusionEngine prod = ProductionFusion();
        FusionEngine reference = ReferenceFusionWithoutStructuralBreak();
        var refState = new FusionStateManager();
        DecisionEngine refDecision = ReferenceDecision();

        var rows = new List<BarRow>(series.Count);

        foreach (BacktestSignalResult bar in result.Bars)
        {
            // Feed BOTH parallel FusionStateManagers on every non-rejected bar (warm-up INCLUDED) so their
            // EMA/hysteresis state stays aligned with what production accumulated - exactly the pattern of
            // StructuralBreakRegressionTests. Only record/compare on Ready bars with a Decision.
            if (bar.Status is BacktestSignalStatus.Rejected or BacktestSignalStatus.Exception) continue;
            if (bar.Regime is null) continue;

            FusionContext ctx = new()
            {
                Evidence = bar.Regime, Timestamp = bar.Timestamp, Symbol = series.Symbol,
                TimeFrame = series.TimeFrame, EvaluationId = Guid.Empty
            };

            FusionResult prodRaw = prod.Fuse(ctx);
            FusionResult refRaw = reference.Fuse(ctx);
            FusionSnapshot refSnap = refState.Update(refRaw, bar.Timestamp);

            // Regression: the 4 evidence dims other than StructuralBreak must be bit-identical between the
            // production (5-rule) and the reference (4-rule) raw fusion output. Assert.Fail (not a plain
            // exception) so a real regression is never swallowed by the outer network-skip catch.
            foreach (FusionDimension d in OtherDims)
            {
                double p = prodRaw.Dimensions[d].Value;
                double q = refRaw.Dimensions[d].Value;
                if (BitConverter.DoubleToInt64Bits(p) != BitConverter.DoubleToInt64Bits(q))
                    Assert.Fail(
                        $"Regression: dimension {d} raw Value differs with vs without StructuralBreakEvidenceRule at bar {bar.BarIndex}: {p} vs {q}.");
            }

            if (bar.Status != BacktestSignalStatus.Ready || bar.Decision is null) continue;

            DecisionResult refDecisionResult = refDecision.Evaluate(new DecisionContext
            {
                FusionResult = refSnap.StableResult, Evidence = bar.Regime
            });

            var cusum = bar.Regime.Cusum;
            double r = 0.0;
            if (cusum is { IsValid: true } && cusum.Threshold > 0.0)
                r = Math.Max(cusum.PositiveCusum, Math.Abs(cusum.NegativeCusum)) / cusum.Threshold;

            rows.Add(new BarRow(
                bar.BarIndex,
                cusum is { IsValid: true } && cusum.ChangeDetected,
                r,
                prodRaw.Dimensions[FusionDimension.StructuralBreak].Value,
                OtherDims.Select(d => refRaw.Dimensions[d].Value).ToArray(),
                bar.Decision.Winner,
                refDecisionResult.Winner));
        }

        return rows;
    }

    private static double Median(List<double> xs)
    {
        var s = xs.OrderBy(v => v).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : 0.5 * (s[s.Count / 2 - 1] + s[s.Count / 2]);
    }

    private static string HashStrengths(List<BarRow> rows)
    {
        var sb = new StringBuilder();
        foreach (BarRow r in rows)
            sb.Append(r.BarIndex).Append('=').Append(BitConverter.DoubleToInt64Bits(r.Strength).ToString("X16")).Append(';');
        return Sha256Hex(sb.ToString());
    }

    private static string HashOtherDims(List<BarRow> rows)
    {
        var sb = new StringBuilder();
        foreach (BarRow r in rows)
        {
            sb.Append(r.BarIndex).Append(':');
            foreach (double v in r.OtherRaw) sb.Append(BitConverter.DoubleToInt64Bits(v).ToString("X16")).Append(',');
            sb.Append(';');
        }
        return Sha256Hex(sb.ToString());
    }

    private static string Sha256Hex(string s)
    {
        byte[] b = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(b.Length * 2);
        foreach (byte x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }
}
