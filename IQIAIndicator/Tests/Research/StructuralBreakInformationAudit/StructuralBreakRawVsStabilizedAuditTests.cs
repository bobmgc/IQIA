using IQIAIndicator.Tests.Research.StructuralBreakInformationAudit.Support;
using Xunit;

namespace IQIAIndicator.Tests.Research.StructuralBreakInformationAudit;

/// <summary>
/// Lot 15.9, brief Section B.9: raw vs stabilized information loss on the continuous
/// StructuralBreak value (NOT the Detected boolean - that is Lot 15.8's RawDetectedTransitions, a
/// different measurement). AUDIT-ONLY.
/// </summary>
public sealed class StructuralBreakRawVsStabilizedAuditTests
{
    private readonly ITestOutputHelper _output;

    public StructuralBreakRawVsStabilizedAuditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void B9_RawVsStabilized_Variance_FrozenRate_LongestFrozenRun_TransitionRatio()
    {
        AuditDataset? dataset = StructuralBreakObservationSetBuilder.Instance;
        if (dataset is null)
        {
            Assert.Skip("Yahoo provider unavailable (not a code failure).");
        }

        IReadOnlyList<ObservationRecord> obs = dataset.Observations;
        int n = obs.Count;
        int sbIndex = StructuralBreakObservationSetBuilder.StructuralBreakDimensionIndex;

        List<double> rawSb = obs.Select(o => o.RawValue[sbIndex]).ToList();
        List<double> stableSb = obs.Select(o => o.StableValue[sbIndex]).ToList();

        object?[] rawRow = BuildSeriesRow("RawValue_StructuralBreak", rawSb);
        object?[] stableRow = BuildSeriesRow("StableValue_StructuralBreak", stableSb);

        int rawTransitionCount = (int)(long)rawRow[6]!;
        int stableTransitionCount = (int)(long)stableRow[6]!;
        double? ratio = rawTransitionCount > 0 ? Math.Round((double)stableTransitionCount / rawTransitionCount, 6) : (double?)null;

        var rows = new List<object?[]> { rawRow, stableRow };

        string outputDir = AuditCsv.ResolveOutputDirectory();
        var csvRows = new List<object?[]>(rows) { new object?[] { "RATIO", n, null, null, null, null, ratio, null } };
        AuditCsv.WriteCsv(Path.Combine(outputDir, "StructuralBreakRawVsStabilized.csv"),
            dataset.Fingerprint,
            new[] { "Series", "N", "Variance", "FrozenCount", "FrozenPct", "LongestFrozenRun", "TransitionCountOrRatio", "TransitionPct" },
            csvRows);

        _output.WriteLine("=== B.9: RAW VS STABILIZED (continuous value, not the Detected boolean) ===");
        _output.WriteLine("Series | N | Variance | FrozenCount | FrozenPct% | LongestFrozenRun | TransitionCount | TransitionPct%");
        foreach (object?[] r in rows) _output.WriteLine(string.Join(" | ", r.Select(AuditCsv.FormatCell)));
        _output.WriteLine($"RATIO (StabilizedTransitions / RawTransitions) = {(ratio.HasValue ? ratio.Value.ToString("F6") : "n/a (0 raw transitions)")}");

        AuditCsv.AssertNoNaNOrInfinity(rows);
        Assert.True(stableTransitionCount <= rawTransitionCount || rawTransitionCount == 0,
            "Stabilization (EMA+hysteresis) should never increase the raw transition count on the same underlying signal.");
    }

    /// <summary>Returns {Series, N, Variance, FrozenCount, FrozenPct, LongestFrozenRun, TransitionCount, TransitionPct}.
    /// TransitionCount is boxed as <see cref="long"/> so the caller can safely unbox it regardless of
    /// which numeric column position it sits at once combined with the RATIO row above.</summary>
    private static object?[] BuildSeriesRow(string seriesName, List<double> values)
    {
        int n = values.Count;
        List<PlateauRun<double>> plateaus = AuditMath.ComputePlateaus(values);
        int longestFrozenRun = plateaus.Count > 0 ? plateaus.Max(p => p.Length) : 0;

        int frozenCount = 0;
        int transitionCount = 0;
        for (int i = 1; i < n; i++)
        {
            if (values[i] == values[i - 1]) frozenCount++;
            else transitionCount++;
        }
        int consecutivePairs = Math.Max(0, n - 1);

        return new object?[]
        {
            seriesName, n,
            Math.Round(AuditMath.Variance(values), 9),
            frozenCount,
            AuditMath.Pct(frozenCount, consecutivePairs),
            longestFrozenRun,
            (long)transitionCount,
            AuditMath.Pct(transitionCount, consecutivePairs)
        };
    }
}
