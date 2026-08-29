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
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Risk;
using Xunit;
using FusionEngine = IQIAIndicator.Engine.Fusion.EvidenceFusionEngine;
using IQIAIndicator.Tests.BacktestTests.Yahoo;

namespace IQIAIndicator.Tests.Research.StructuralBreakAudit;

/// <summary>
/// Sprint 15.25 (Lot 15.8, "StructuralBreak Evidence Implementation" - MANDATORY empirical measurement).
/// Runs the real, unmodified <see cref="BacktestEngine.RunSignalPipeline"/> (which now includes
/// <see cref="StructuralBreakEvidenceRule"/>) over the current real Yahoo MES M5 dataset (~59 days / ~11400
/// bars, same recipe as every prior Lot 15.x Yahoo test) and reports plain descriptive statistics - no
/// calibration, no threshold selection, no "best" anything.
///
/// The stabilized <see cref="FusionDimension.StructuralBreak"/> value is not itself exposed on
/// <see cref="BacktestSignalResult"/> (only the already-arbitrated <c>Decision</c> is) - as in
/// <see cref="Pipeline.StructuralBreakFusionIntegrationTests"/>/<see cref="Pipeline.StructuralBreakLookAheadTests"/>,
/// it is reconstructed via a separate, production-equivalent (same rule list, same order)
/// <see cref="FusionEngine"/> + <see cref="FusionStateManager"/> fed bar-by-bar from each bar's own
/// already-computed <see cref="EvidenceSet"/>.
/// </summary>
public sealed class StructuralBreakEvidenceLot158IntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public StructuralBreakEvidenceLot158IntegrationTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static readonly MarketState[] AllRegimes = Enum.GetValues<MarketState>();

    private static FusionEngine BuildProductionEquivalentFusionEngine() => new(new IFusionRule[]
    {
        new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new RandomWalkRule(),
        new StructuralBreakEvidenceRule()
    });

    private sealed class BarObservation
    {
        public required int BarIndex;
        public required MarketState Winner;
        public required bool Detected;
        public required double Strength;
        public required int BreakCountMagnitude;
        public required StructuralBreakAgreement Agreement;
        public required bool IsAvailable;
        public required double StableValue;
        public required bool StableIsAvailable;
    }

    [Fact]
    public void Integration_Network_StructuralBreakEvidence_DescriptiveMeasurement_Lot158()
    {
        try
        {
            HistoricalSeries series = _yahoo.Require();
            Assert.True(series.Count > 0);

            _output.WriteLine("=== DATASET IDENTITY (Lot 15.8) ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");

            const int warmupBars = 128;
            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT15.8-FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            BacktestSignalPipelineResult signalResult = new BacktestEngine().RunSignalPipeline(scenario, warmupBars);
            Assert.Equal(0, signalResult.ExceptionCount);
            _output.WriteLine($"BarsProcessed={signalResult.BarsProcessed}, BarsRejected={signalResult.BarsRejected}, WarmupBars={signalResult.WarmupBars}, ReadyBars={signalResult.ReadyBars}, TotalSeriesBars={series.Count}");

            FusionEngine fusionEngine = BuildProductionEquivalentFusionEngine();
            var fusionState = new FusionStateManager();
            var observations = new List<BarObservation>(series.Count);

            foreach (BacktestSignalResult bar in signalResult.Bars)
            {
                if (bar.Status is BacktestSignalStatus.Rejected or BacktestSignalStatus.Exception) continue;
                if (bar.Regime is null || bar.Decision is null) continue;

                StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(bar.Regime.Cusum, bar.Regime.BaiPerron);

                FusionResult raw = fusionEngine.Fuse(new FusionContext
                {
                    Evidence = bar.Regime, Timestamp = bar.Timestamp, Symbol = series.Symbol, TimeFrame = series.TimeFrame, EvaluationId = Guid.Empty
                });
                FusionSnapshot snapshot = fusionState.Update(raw, bar.Timestamp);
                FusionConfidence stable = snapshot.StableResult.Dimensions[FusionDimension.StructuralBreak];

                observations.Add(new BarObservation
                {
                    BarIndex = bar.BarIndex,
                    Winner = bar.Decision.Winner,
                    Detected = contract.Detected,
                    Strength = contract.Strength,
                    BreakCountMagnitude = contract.BreakCountMagnitude,
                    Agreement = contract.Agreement,
                    IsAvailable = contract.IsAvailable,
                    StableValue = stable.Value,
                    StableIsAvailable = stable.IsAvailable
                });
            }

            _output.WriteLine($"ObservedBars={observations.Count} (expected == BarsProcessed {signalResult.BarsProcessed})");
            Assert.Equal(signalResult.BarsProcessed, observations.Count);

            string outputDir = ResolveOutputDirectory();
            string hash1 = RunAggregationAndReport(observations, series.Count, outputDir, print: true);
            string hash2 = RunAggregationAndReport(observations, series.Count, outputDir, print: false);
            _output.WriteLine("");
            _output.WriteLine($"=== DETERMINISM CHECK === Hash1={hash1}, Hash2={hash2}, Identical={hash1 == hash2}");
            Assert.Equal(hash1, hash2);
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ──────────────────────────────────────────── Aggregation + CSV ───────────────────────────────────────

    private string RunAggregationAndReport(List<BarObservation> observations, int totalSeriesBars, string outputDir, bool print)
    {
        var hashInput = new StringBuilder();
        var rows = new List<object?[]>();

        // ── OVERALL ──────────────────────────────────────────────────────────────────────────────────────
        int n = observations.Count;
        int availableCount = observations.Count(o => o.IsAvailable);
        int detectedCount = observations.Count(o => o.Detected);
        rows.Add(new object?[] { "OVERALL", "TotalSeriesBars", totalSeriesBars, null, null });
        rows.Add(new object?[] { "OVERALL", "ObservedBars", n, null, null });
        rows.Add(new object?[] { "OVERALL", "IsAvailableCount", availableCount, Pct(availableCount, n), null });
        rows.Add(new object?[] { "OVERALL", "DetectedCount_RawPreStabilization", detectedCount, Pct(detectedCount, n), null });
        rows.Add(new object?[] { "OVERALL", "DetectedPctAmongAvailable", detectedCount, availableCount > 0 ? Pct(detectedCount, availableCount) : (double?)null, null });

        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== SECTION: OVERALL ===");
            foreach (object?[] r in rows) _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── STRENGTH distribution when Detected=true ────────────────────────────────────────────────────
        List<double> strengthWhenDetected = observations.Where(o => o.Detected).Select(o => o.Strength).OrderBy(v => v).ToList();
        rows.Add(new object?[]
        {
            "STRENGTH_WHEN_DETECTED", strengthWhenDetected.Count,
            strengthWhenDetected.Count > 0 ? Math.Round(strengthWhenDetected.Average(), 6) : (double?)null,
            strengthWhenDetected.Count > 0 ? Math.Round(Percentile(strengthWhenDetected, 0.5), 6) : (double?)null,
            strengthWhenDetected.Count > 0 ? strengthWhenDetected.Min() : (double?)null,
            strengthWhenDetected.Count > 0 ? strengthWhenDetected.Max() : (double?)null
        });
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== SECTION: STRENGTH_WHEN_DETECTED (N, Mean, Median, Min, Max) ===");
            _output.WriteLine(string.Join(" | ", rows.Last().Select(FormatCell)));
        }

        // ── BreakCountMagnitude distribution ────────────────────────────────────────────────────────────
        List<double> magnitudeAll = observations.Select(o => (double)o.BreakCountMagnitude).OrderBy(v => v).ToList();
        List<double> magnitudeWhenDetected = observations.Where(o => o.Detected).Select(o => (double)o.BreakCountMagnitude).OrderBy(v => v).ToList();
        rows.Add(new object?[]
        {
            "BREAKCOUNTMAGNITUDE_DISTRIBUTION", "AllBars", magnitudeAll.Count,
            magnitudeAll.Count > 0 ? Math.Round(magnitudeAll.Average(), 6) : (double?)null,
            magnitudeAll.Count > 0 ? Math.Round(Percentile(magnitudeAll, 0.5), 6) : (double?)null,
            magnitudeAll.Count > 0 ? magnitudeAll.Min() : (double?)null,
            magnitudeAll.Count > 0 ? magnitudeAll.Max() : (double?)null
        });
        rows.Add(new object?[]
        {
            "BREAKCOUNTMAGNITUDE_DISTRIBUTION", "AmongDetected", magnitudeWhenDetected.Count,
            magnitudeWhenDetected.Count > 0 ? Math.Round(magnitudeWhenDetected.Average(), 6) : (double?)null,
            magnitudeWhenDetected.Count > 0 ? Math.Round(Percentile(magnitudeWhenDetected, 0.5), 6) : (double?)null,
            magnitudeWhenDetected.Count > 0 ? magnitudeWhenDetected.Min() : (double?)null,
            magnitudeWhenDetected.Count > 0 ? magnitudeWhenDetected.Max() : (double?)null
        });
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== SECTION: BREAKCOUNTMAGNITUDE_DISTRIBUTION (Scope, N, Mean, Median, Min, Max) ===");
            foreach (object?[] r in rows.Where(r => (string)r[0]! == "BREAKCOUNTMAGNITUDE_DISTRIBUTION"))
                _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Agreement distribution ───────────────────────────────────────────────────────────────────────
        foreach (StructuralBreakAgreement agreement in Enum.GetValues<StructuralBreakAgreement>())
        {
            int count = observations.Count(o => o.Agreement == agreement);
            rows.Add(new object?[] { "AGREEMENT_DISTRIBUTION", agreement.ToString(), count, Pct(count, n), null });
        }
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== SECTION: AGREEMENT_DISTRIBUTION (Agreement, Count, Pct%) ===");
            foreach (object?[] r in rows.Where(r => (string)r[0]! == "AGREEMENT_DISTRIBUTION"))
                _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Raw transitions (Detected flips bar-to-bar) vs stabilized transitions (StructuralBreak's own
        // stable Value changes bar-to-bar - i.e. the EMA+hysteresis in FusionStateManager actually let a
        // new value through for THIS dimension specifically; exact equality is safe here because
        // FusionStateManager.BuildStableResult either keeps the previous Value object's field bit-for-bit
        // unchanged (hysteresis blocked) or replaces it with the freshly-smoothed value - never a
        // no-op recomputation of an equal value). ──────────────────────────────────────────────────────────
        int rawTransitions = 0;
        int stabilizedTransitions = 0;
        for (int i = 1; i < observations.Count; i++)
        {
            if (observations[i].Detected != observations[i - 1].Detected) rawTransitions++;
            if (observations[i].StableValue != observations[i - 1].StableValue) stabilizedTransitions++;
        }
        int consecutivePairs = Math.Max(0, observations.Count - 1);
        rows.Add(new object?[] { "TRANSITIONS", "RawDetectedTransitions", rawTransitions, Pct(rawTransitions, consecutivePairs), consecutivePairs });
        rows.Add(new object?[] { "TRANSITIONS", "StabilizedValueTransitions", stabilizedTransitions, Pct(stabilizedTransitions, consecutivePairs), consecutivePairs });
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== SECTION: TRANSITIONS (Label, Count, Pct% of consecutive pairs, TotalConsecutivePairs) ===");
            foreach (object?[] r in rows.Where(r => (string)r[0]! == "TRANSITIONS"))
                _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        // ── Per-regime breakdown ─────────────────────────────────────────────────────────────────────────
        foreach (MarketState regime in AllRegimes)
        {
            List<BarObservation> bars = observations.Where(o => o.Winner == regime).ToList();
            int regimeN = bars.Count;
            int regimeDetected = bars.Count(o => o.Detected);
            List<double> regimeStrengthDetected = bars.Where(o => o.Detected).Select(o => o.Strength).ToList();
            rows.Add(new object?[]
            {
                "REGIME_CONDITIONAL", regime.ToString(), regimeN,
                Pct(regimeDetected, regimeN),
                regimeStrengthDetected.Count > 0 ? Math.Round(regimeStrengthDetected.Average(), 6) : (double?)null,
                regimeN < 30 ? "LOW_N_UNRELIABLE" : "OK"
            });
        }
        if (print)
        {
            _output.WriteLine("");
            _output.WriteLine("=== SECTION: REGIME_CONDITIONAL (Regime, N, PctDetectedRaw%, MeanStrengthAmongDetected, ReliabilityFlag) ===");
            foreach (object?[] r in rows.Where(r => (string)r[0]! == "REGIME_CONDITIONAL"))
                _output.WriteLine(string.Join(" | ", r.Select(FormatCell)));
        }

        WriteCsv(Path.Combine(outputDir, "structuralbreak_evidence_lot158.csv"),
            new[] { "Section", "Col1", "Col2", "Col3", "Col4", "Col5" }, rows);
        AppendHash(hashInput, rows);

        foreach (object?[] row in rows)
            foreach (object? cell in row)
                if (cell is double d) Assert.True(double.IsFinite(d), $"NaN/Infinity found in a report cell: {d}");

        return Sha256Hex(hashInput.ToString());
    }

    // ─────────────────────────────────────────────── Helpers ──────────────────────────────────────────────

    private static double Pct(int count, int total) => total > 0 ? Math.Round(100.0 * count / total, 3) : 0.0;

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0.0;
        if (sorted.Count == 1) return sorted[0];
        double rank = p * (sorted.Count - 1);
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sorted[lower];
        double fraction = rank - lower;
        return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
    }

    private static string ResolveOutputDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;

        if (dir is null)
            throw new InvalidOperationException("Could not locate IQIAIndicator.Tests.csproj to resolve the StructuralBreakAudit output directory.");

        string outputDir = Path.Combine(dir, "Research", "StructuralBreakAudit", "Output");
        Directory.CreateDirectory(outputDir);
        return outputDir;
    }

    private static void WriteCsv(string path, string[] headers, List<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(CsvCell)));
        foreach (object?[] row in rows)
        {
            var padded = new object?[headers.Length];
            for (int i = 0; i < headers.Length; i++)
                padded[i] = i < row.Length ? row[i] : null;
            sb.AppendLine(string.Join(",", padded.Select(CsvCell)));
        }
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
        DateTime dt => dt.ToString("O"),
        bool b => b.ToString(),
        _ => value.ToString() ?? ""
    };

    private static void AppendHash(StringBuilder sb, List<object?[]> rows)
    {
        foreach (object?[] row in rows)
            sb.Append(string.Join("|", row.Select(FormatCell))).Append(';');
    }

    private static string Sha256Hex(string s)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException;
}
