using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Tests.Research.StructuralBreakInformationAudit.Support;
using Xunit;

namespace IQIAIndicator.Tests.Research.StructuralBreakInformationAudit;

/// <summary>
/// Lot 15.9, brief Sections B.6 (unique information test) and B.11 (information availability, offline
/// only - no BUY/SELL/P&amp;L simulation). AUDIT-ONLY.
/// </summary>
public sealed class StructuralBreakInformationContentAuditTests
{
    private readonly ITestOutputHelper _output;

    public StructuralBreakInformationContentAuditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ═══════════════════════════════════ B.6: unique information ══════════════════════════════════════════

    [Fact]
    public void B6_UniqueInformation_StructuralBreakTransitionsAlone_Vs_OthersAlone()
    {
        AuditDataset? dataset = StructuralBreakObservationSetBuilder.Instance;
        if (dataset is null)
        {
            Assert.Skip("Yahoo provider unavailable (not a code failure).");
        }

        IReadOnlyList<ObservationRecord> obs = dataset.Observations;
        int n = obs.Count;
        int sbIndex = StructuralBreakObservationSetBuilder.StructuralBreakDimensionIndex;
        FusionDimension[] allDims = StructuralBreakObservationSetBuilder.AllDimensions;
        int[] otherIndices = Enumerable.Range(0, allDims.Length).Where(i => i != sbIndex).ToArray();

        var sbTransitions = new bool[n];
        var anyOtherTransitions = new bool[n];
        for (int i = 1; i < n; i++)
        {
            sbTransitions[i] = obs[i].StableValue[sbIndex] != obs[i - 1].StableValue[sbIndex];
            anyOtherTransitions[i] = otherIndices.Any(idx => obs[i].StableValue[idx] != obs[i - 1].StableValue[idx]);
        }

        int consecutivePairs = Math.Max(0, n - 1);
        int sbOnlyCount = 0, othersOnlyCount = 0;
        var sbOnlyEvents = new List<(int Index, ObservationRecord Bar)>();
        var othersOnlyEvents = new List<(int Index, ObservationRecord Bar)>();
        for (int i = 1; i < n; i++)
        {
            bool sb = sbTransitions[i];
            bool others = anyOtherTransitions[i];
            if (sb && !others) { sbOnlyCount++; sbOnlyEvents.Add((i, obs[i])); }
            if (!sb && others) { othersOnlyCount++; othersOnlyEvents.Add((i, obs[i])); }
        }

        int sbTransitionCount = sbTransitions.Count(t => t);
        int anyOtherTransitionCount = anyOtherTransitions.Count(t => t);

        var summaryRows = new List<object?[]>
        {
            new object?[]
            {
                "SB_TRANSITIONS_ALONE_UNIQUE_INFO", sbOnlyCount, AuditMath.Pct(sbOnlyCount, consecutivePairs),
                sbTransitionCount > 0 ? AuditMath.Pct(sbOnlyCount, sbTransitionCount) : (double?)null,
                consecutivePairs, sbTransitionCount
            },
            new object?[]
            {
                "OTHERS_TRANSITION_ALONE_SB_SILENT", othersOnlyCount, AuditMath.Pct(othersOnlyCount, consecutivePairs),
                anyOtherTransitionCount > 0 ? AuditMath.Pct(othersOnlyCount, anyOtherTransitionCount) : (double?)null,
                consecutivePairs, anyOtherTransitionCount
            }
        };

        // ── Temporal distribution: weekly buckets (ISO calendar week of the bar's UTC timestamp) - are
        // "SB-alone" unique-information events concentrated or dispersed across the dataset span? ────────
        var weeklyRows = new List<object?[]>();
        foreach (var (label, events) in new[] { ("SB_ONLY", sbOnlyEvents), ("OTHERS_ONLY", othersOnlyEvents) })
        {
            var byWeek = events
                .GroupBy(e => System.Globalization.ISOWeek.GetWeekOfYear(e.Bar.Timestamp))
                .OrderBy(g => g.Key);
            foreach (var g in byWeek)
                weeklyRows.Add(new object?[] { label, g.Key, g.Count() });
        }

        string outputDir = AuditCsv.ResolveOutputDirectory();
        var allRows = new List<object?[]>();
        allRows.AddRange(summaryRows.Select(r => new object?[] { "SUMMARY", r[0], r[1], r[2], r[3], r[4], r[5] }));
        allRows.AddRange(weeklyRows.Select(r => new object?[] { "WEEKLY_DISTRIBUTION", r[0], r[1], r[2], null, null, null }));

        AuditCsv.WriteCsv(Path.Combine(outputDir, "StructuralBreakTransitions.csv"),
            dataset.Fingerprint,
            new[] { "RowType", "Label", "Count", "PctOfConsecutivePairsOrIsoWeek", "PctOfOwnTransitionCount", "TotalConsecutivePairs", "TransitionCountUsedAsDenominator" },
            allRows);

        _output.WriteLine("=== B.6: UNIQUE INFORMATION (StableValue transitions, exact-equality, Lot 15.8 definition) ===");
        _output.WriteLine($"N={n}, ConsecutivePairs={consecutivePairs}, SBTransitionCount={sbTransitionCount}, AnyOtherTransitionCount={anyOtherTransitionCount}");
        _output.WriteLine("RowType | Count | PctOfConsecutivePairs% | PctOfOwnTransitionCount% | TotalConsecutivePairs | Denominator");
        foreach (object?[] r in summaryRows) _output.WriteLine(string.Join(" | ", r.Select(AuditCsv.FormatCell)));
        _output.WriteLine("");
        _output.WriteLine("=== B.6: TEMPORAL DISTRIBUTION (ISO week of year -> event count) ===");
        foreach (object?[] r in weeklyRows) _output.WriteLine(string.Join(" | ", r.Select(AuditCsv.FormatCell)));

        AuditCsv.AssertNoNaNOrInfinity(summaryRows);
        // Structural sanity: SB-only + (SB-and-others) must equal total SB transitions.
        int sbAndOthers = 0;
        for (int i = 1; i < n; i++) if (sbTransitions[i] && anyOtherTransitions[i]) sbAndOthers++;
        Assert.Equal(sbTransitionCount, sbOnlyCount + sbAndOthers);
    }

    // ═══════════════════════════════════ B.11: information availability ═══════════════════════════════════

    [Fact]
    public void B11_InformationAvailability_DetectedRate_IsAvailableRate_UnsupportedRegimes()
    {
        AuditDataset? dataset = StructuralBreakObservationSetBuilder.Instance;
        if (dataset is null)
        {
            Assert.Skip("Yahoo provider unavailable (not a code failure).");
        }

        IReadOnlyList<ObservationRecord> obs = dataset.Observations;
        int n = obs.Count;
        int detectedCount = obs.Count(o => o.Contract.Detected);
        int availableCount = obs.Count(o => o.Contract.IsAvailable);

        MarketState[] unsupported = StructuralBreakObservationSetBuilder.UnsupportedEntryRegimes;
        List<ObservationRecord> unsupportedBars = obs.Where(o => unsupported.Contains(o.Winner)).ToList();
        int unsupportedDetectedCount = unsupportedBars.Count(o => o.Contract.Detected);

        var rows = new List<object?[]>
        {
            new object?[] { "OVERALL", "PctDetected", detectedCount, AuditMath.Pct(detectedCount, n), n },
            new object?[] { "OVERALL", "PctIsAvailable", availableCount, AuditMath.Pct(availableCount, n), n },
            new object?[]
            {
                "UNSUPPORTED_ENTRY_REGIMES_UNION", "PctDetected", unsupportedDetectedCount,
                AuditMath.Pct(unsupportedDetectedCount, unsupportedBars.Count), unsupportedBars.Count
            }
        };

        foreach (MarketState regime in unsupported)
        {
            List<ObservationRecord> bars = obs.Where(o => o.Winner == regime).ToList();
            int det = bars.Count(o => o.Contract.Detected);
            rows.Add(new object?[] { "UNSUPPORTED_ENTRY_REGIME_BREAKDOWN", regime.ToString(), det, AuditMath.Pct(det, bars.Count), bars.Count });
        }

        string outputDir = AuditCsv.ResolveOutputDirectory();
        AuditCsv.WriteCsv(Path.Combine(outputDir, "InformationAvailability.csv"),
            dataset.Fingerprint,
            new[] { "Scope", "Metric", "Count", "Pct", "Denominator" },
            rows);

        _output.WriteLine("=== B.11: INFORMATION AVAILABILITY (offline counts only, no P&L/BUY/SELL simulation) ===");
        _output.WriteLine($"N={n}, UnsupportedEntryRegimes={string.Join(",", unsupported)}, UnsupportedBarsN={unsupportedBars.Count}");
        _output.WriteLine("Scope | Metric | Count | Pct% | Denominator");
        foreach (object?[] r in rows) _output.WriteLine(string.Join(" | ", r.Select(AuditCsv.FormatCell)));

        AuditCsv.AssertNoNaNOrInfinity(rows);
        Assert.True(availableCount >= detectedCount, "Detected==true bars must be a subset of IsAvailable==true bars (BuildContract sets Detected only when IsAvailable).");
    }
}
