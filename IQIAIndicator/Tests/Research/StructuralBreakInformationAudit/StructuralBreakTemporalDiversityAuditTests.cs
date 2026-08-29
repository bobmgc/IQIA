using IQIAIndicator.Tests.Research.StructuralBreakInformationAudit.Support;
using Xunit;

namespace IQIAIndicator.Tests.Research.StructuralBreakInformationAudit;

/// <summary>
/// Lot 15.9, brief Section B.10: temporal diversity of StableValue[StructuralBreak], read as a sequence of
/// "states" via its own plateaus (a constant run = one state). AUDIT-ONLY.
/// </summary>
public sealed class StructuralBreakTemporalDiversityAuditTests
{
    private readonly ITestOutputHelper _output;

    public StructuralBreakTemporalDiversityAuditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void B10_TemporalDiversity_StableValueStates_DurationsAndDailyChangeFrequency()
    {
        AuditDataset? dataset = StructuralBreakObservationSetBuilder.Instance;
        if (dataset is null)
        {
            Assert.Skip("Yahoo provider unavailable (not a code failure).");
        }

        IReadOnlyList<ObservationRecord> obs = dataset.Observations;
        int n = obs.Count;
        int sbIndex = StructuralBreakObservationSetBuilder.StructuralBreakDimensionIndex;

        List<double> stableSb = obs.Select(o => o.StableValue[sbIndex]).ToList();
        List<PlateauRun<double>> states = AuditMath.ComputePlateaus(stableSb);
        List<double> durations = states.Select(s => (double)s.Length).ToList();
        int stateChangeCount = Math.Max(0, states.Count - 1);

        var summaryRows = new List<object?[]>
        {
            new object?[]
            {
                "SUMMARY", n, states.Count, stateChangeCount,
                Math.Round(durations.Average(), 6),
                Math.Round(AuditMath.Percentile(durations.OrderBy(v => v).ToList(), 0.5), 6),
                durations.Max(),
                Math.Round(AuditMath.Pct(stateChangeCount, Math.Max(0, n - 1)), 6)
            }
        };

        // ── Per-calendar-day (UTC date) count of state changes: a change is attributed to the day of the
        // bar where the NEW state begins (i.e. the plateau's StartIndex, excluding the very first plateau
        // which has no "change into" it - it is the initial state). ─────────────────────────────────────
        var dateCounts = new SortedDictionary<DateTime, int>();
        for (int i = 1; i < states.Count; i++)
        {
            DateTime date = obs[states[i].StartIndex].Timestamp.Date;
            dateCounts.TryGetValue(date, out int c);
            dateCounts[date] = c + 1;
        }

        var dailyRows = dateCounts.Select(kv => new object?[] { "DAILY_CHANGE_COUNT", kv.Key, kv.Value }).ToList();

        int totalDays = dataset.Series.Count > 0
            ? (int)(dataset.Series.LastTimestamp.Date - dataset.Series.FirstTimestamp.Date).TotalDays + 1
            : 0;
        int daysWithAtLeastOneChange = dateCounts.Count;

        string outputDir = AuditCsv.ResolveOutputDirectory();
        var allRows = new List<object?[]>();
        allRows.AddRange(summaryRows.Select(r => new object?[] { "SUMMARY", r[1], r[2], r[3], r[4], r[5], r[6], r[7] }));
        allRows.Add(new object?[] { "COVERAGE", totalDays, daysWithAtLeastOneChange, AuditMath.Pct(daysWithAtLeastOneChange, totalDays), null, null, null, null });
        allRows.AddRange(dailyRows.Select(r => new object?[] { "DAILY_CHANGE_COUNT", r[1], r[2], null, null, null, null, null }));

        AuditCsv.WriteCsv(Path.Combine(outputDir, "StructuralBreakTemporalDiversity.csv"),
            dataset.Fingerprint,
            new[] { "RowType", "Col1_DateOrN", "Col2_CountOrStates", "Col3", "Col4", "Col5", "Col6", "Col7" },
            allRows);

        _output.WriteLine("=== B.10: TEMPORAL DIVERSITY (StableValue[StructuralBreak] read as a sequence of plateau-states) ===");
        _output.WriteLine($"N={n}, StateCount={states.Count}, StateChangeCount={stateChangeCount}, ChangeFrequencyPctOfConsecutivePairs={AuditMath.Pct(stateChangeCount, Math.Max(0, n - 1))}%");
        _output.WriteLine($"MeanStateDuration={durations.Average():F3} bars, MedianStateDuration={AuditMath.Percentile(durations.OrderBy(v => v).ToList(), 0.5):F3} bars, MaxStateDuration={durations.Max()} bars");
        _output.WriteLine($"DatasetSpan: TotalCalendarDays={totalDays}, DaysWithAtLeastOneStateChange={daysWithAtLeastOneChange} ({AuditMath.Pct(daysWithAtLeastOneChange, totalDays)}%)");
        _output.WriteLine("Date | StateChangeCountThatDay");
        foreach (object?[] r in dailyRows) _output.WriteLine(string.Join(" | ", new[] { r[1], r[2] }.Select(AuditCsv.FormatCell)));

        AuditCsv.AssertNoNaNOrInfinity(allRows);
        Assert.Equal(states.Sum(s => s.Length), n);
        Assert.Equal(dateCounts.Values.Sum(), stateChangeCount);
    }
}
