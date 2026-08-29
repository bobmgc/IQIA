using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Tests.Research.StructuralBreakInformationAudit.Support;
using Xunit;

namespace IQIAIndicator.Tests.Research.StructuralBreakInformationAudit;

/// <summary>
/// Lot 15.9, brief Sections B.7 (per-regime conditional analysis) and B.8 (MarketState transition event
/// study - explicitly POST-HOC, never presented as causal/real-time). Named "...Yahoo..." per the brief's
/// suggestion that this can be the "hat" class, but it does NOT reload Yahoo itself - it consumes the same
/// single shared <see cref="StructuralBreakObservationSetBuilder.Instance"/> every other class in this
/// folder uses. AUDIT-ONLY.
/// </summary>
public sealed class StructuralBreakYahooInformationAuditTests
{
    private readonly ITestOutputHelper _output;

    public StructuralBreakYahooInformationAuditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ═══════════════════════════════════ B.7: per-regime conditional analysis ════════════════════════════

    [Fact]
    public void B7_RegimeConditionalAnalysis_DetectedRate_Strength_BreakCount_Agreement()
    {
        AuditDataset? dataset = StructuralBreakObservationSetBuilder.Instance;
        if (dataset is null)
        {
            Assert.Skip("Yahoo provider unavailable (not a code failure).");
        }

        IReadOnlyList<ObservationRecord> obs = dataset.Observations;
        var rows = new List<object?[]>();

        foreach (MarketState regime in StructuralBreakObservationSetBuilder.AllRegimes)
        {
            List<ObservationRecord> bars = obs.Where(o => o.Winner == regime).ToList();
            int regimeN = bars.Count;
            int detected = bars.Count(o => o.Contract.Detected);
            List<double> strengthDetected = bars.Where(o => o.Contract.Detected).Select(o => o.Contract.Strength).ToList();
            List<double> breakCounts = bars.Select(o => (double)o.Contract.BreakCountMagnitude).ToList();

            int both = bars.Count(o => o.Contract.Agreement == StructuralBreakAgreement.Both);
            int cusumOnly = bars.Count(o => o.Contract.Agreement == StructuralBreakAgreement.CusumOnly);
            int bpOnly = bars.Count(o => o.Contract.Agreement == StructuralBreakAgreement.BaiPerronOnly);
            int neither = bars.Count(o => o.Contract.Agreement == StructuralBreakAgreement.Neither);
            int unavailable = bars.Count(o => o.Contract.Agreement == StructuralBreakAgreement.Unavailable);

            rows.Add(new object?[]
            {
                regime.ToString(), regimeN,
                AuditMath.Pct(detected, regimeN),
                strengthDetected.Count > 0 ? Math.Round(strengthDetected.Average(), 6) : (double?)null,
                strengthDetected.Count > 0 ? Math.Round(AuditMath.Percentile(strengthDetected.OrderBy(v => v).ToList(), 0.5), 6) : (double?)null,
                regimeN > 0 ? Math.Round(breakCounts.Average(), 6) : (double?)null,
                regimeN > 0 ? Math.Round(AuditMath.Percentile(breakCounts.OrderBy(v => v).ToList(), 0.5), 6) : (double?)null,
                regimeN > 0 ? breakCounts.Min() : (double?)null,
                regimeN > 0 ? breakCounts.Max() : (double?)null,
                AuditMath.Pct(both, regimeN), AuditMath.Pct(cusumOnly, regimeN), AuditMath.Pct(bpOnly, regimeN),
                AuditMath.Pct(neither, regimeN), AuditMath.Pct(unavailable, regimeN),
                regimeN < 30 ? "LOW_N_UNRELIABLE" : "OK"
            });
        }

        string outputDir = AuditCsv.ResolveOutputDirectory();
        AuditCsv.WriteCsv(Path.Combine(outputDir, "RegimeConditionalAnalysis.csv"),
            dataset.Fingerprint,
            new[]
            {
                "Regime", "N", "PctDetected", "MeanStrengthAmongDetected", "MedianStrengthAmongDetected",
                "MeanBreakCount", "MedianBreakCount", "MinBreakCount", "MaxBreakCount",
                "PctBoth", "PctCusumOnly", "PctBaiPerronOnly", "PctNeither", "PctUnavailable", "ReliabilityFlag"
            },
            rows);

        _output.WriteLine("=== B.7: REGIME-CONDITIONAL ANALYSIS (N<30 flagged LOW_N_UNRELIABLE) ===");
        _output.WriteLine("Regime | N | PctDetected% | MeanStrength | MedianStrength | MeanBreakCount | MedianBreakCount | MinBC | MaxBC | %Both | %CusumOnly | %BPOnly | %Neither | %Unavail | Flag");
        foreach (object?[] r in rows) _output.WriteLine(string.Join(" | ", r.Select(AuditCsv.FormatCell)));

        AuditCsv.AssertNoNaNOrInfinity(rows);
        foreach (object?[] r in rows)
        {
            double sum = (double)r[9]! + (double)r[10]! + (double)r[11]! + (double)r[12]! + (double)r[13]!;
            int regimeN = (int)r[1]!;
            if (regimeN > 0) Assert.Equal(100.0, Math.Round(sum, 1));
        }
        Assert.Equal(StructuralBreakObservationSetBuilder.AllRegimes.Length, rows.Count);
    }

    // ═══════════════════════════════════ B.8: MarketState transition event study (post-hoc) ═══════════════

    private static readonly int[] Offsets = { -10, -5, -1, 0, 1, 5, 10 };

    [Fact]
    public void B8_TransitionEventStudy_PostHoc_OffsetAveragedStructuralBreakAndOthers()
    {
        AuditDataset? dataset = StructuralBreakObservationSetBuilder.Instance;
        if (dataset is null)
        {
            Assert.Skip("Yahoo provider unavailable (not a code failure).");
        }

        IReadOnlyList<ObservationRecord> obs = dataset.Observations;
        int n = obs.Count;
        int sbIndex = StructuralBreakObservationSetBuilder.StructuralBreakDimensionIndex;
        int[] otherIndices = Enumerable.Range(0, StructuralBreakObservationSetBuilder.AllDimensions.Length).Where(i => i != sbIndex).ToArray();

        int totalWinnerTransitions = 0;
        var eventIndices = new List<int>();
        for (int t = 1; t < n; t++)
        {
            if (obs[t].Winner == obs[t - 1].Winner) continue;
            totalWinnerTransitions++;
            if (t - 10 < 0 || t + 10 >= n) continue; // brief: only events with the full +-10 window in bounds
            eventIndices.Add(t);
        }

        var perOffsetStable = Offsets.ToDictionary(o => o, _ => new List<double>());
        var perOffsetRaw = Offsets.ToDictionary(o => o, _ => new List<double>());
        var perOffsetOthersMean = Offsets.ToDictionary(o => o, _ => new List<double>());

        foreach (int t in eventIndices)
        {
            foreach (int offset in Offsets)
            {
                int idx = t + offset;
                perOffsetStable[offset].Add(obs[idx].StableValue[sbIndex]);
                perOffsetRaw[offset].Add(obs[idx].RawValue[sbIndex]);
                perOffsetOthersMean[offset].Add(otherIndices.Average(oi => obs[idx].StableValue[oi]));
            }
        }

        var rows = Offsets.Select(offset => new object?[]
        {
            offset, eventIndices.Count,
            perOffsetStable[offset].Count > 0 ? Math.Round(perOffsetStable[offset].Average(), 6) : (double?)null,
            perOffsetRaw[offset].Count > 0 ? Math.Round(perOffsetRaw[offset].Average(), 6) : (double?)null,
            perOffsetOthersMean[offset].Count > 0 ? Math.Round(perOffsetOthersMean[offset].Average(), 6) : (double?)null
        }).ToList();

        string outputDir = AuditCsv.ResolveOutputDirectory();
        AuditCsv.WriteCsv(Path.Combine(outputDir, "TransitionEventStudy.csv"),
            dataset.Fingerprint,
            new[] { "OffsetBars", "EventCount", "MeanStableValue_StructuralBreak", "MeanRawValue_StructuralBreak", "MeanOfOther5DimensionsStableValue" },
            rows);

        _output.WriteLine("=== B.8: MARKETSTATE TRANSITION EVENT STUDY (POST-HOC, NEVER causal/real-time) ===");
        _output.WriteLine($"TotalWinnerTransitions={totalWinnerTransitions}, UsableEvents(+-10 fully in bounds)={eventIndices.Count}");
        _output.WriteLine("OffsetBars | EventCount | MeanStableValue[SB] | MeanRawValue[SB] | MeanOfOther5(StableValue)");
        foreach (object?[] r in rows) _output.WriteLine(string.Join(" | ", r.Select(AuditCsv.FormatCell)));

        AuditCsv.AssertNoNaNOrInfinity(rows);
        Assert.Equal(Offsets.Length, rows.Count);
        foreach (object?[] r in rows) Assert.Equal(eventIndices.Count, (int)r[1]!);
    }
}
