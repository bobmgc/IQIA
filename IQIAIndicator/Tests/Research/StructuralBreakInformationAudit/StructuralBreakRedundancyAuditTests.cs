using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Tests.Research.StructuralBreakInformationAudit.Support;
using Xunit;

namespace IQIAIndicator.Tests.Research.StructuralBreakInformationAudit;

/// <summary>
/// Lot 15.9, brief Section B.5: inter-dimension redundancy between StructuralBreak and each of the other 5
/// Fusion dimensions. AUDIT-ONLY - Pearson/Spearman correlation and transition co-occurrence percentages
/// only, exactly as specified; no composite score invented.
/// </summary>
public sealed class StructuralBreakRedundancyAuditTests
{
    private readonly ITestOutputHelper _output;

    public StructuralBreakRedundancyAuditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void B5_Redundancy_StructuralBreak_Vs_EachOtherDimension_Pearson_Spearman_CoTransition()
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

        List<double> rawSb = obs.Select(o => o.RawValue[sbIndex]).ToList();
        List<double> stableSb = obs.Select(o => o.StableValue[sbIndex]).ToList();

        // StableValue transitions for StructuralBreak (Lot 15.8's exact exact-equality definition).
        var sbTransitions = new bool[n];
        for (int i = 1; i < n; i++) sbTransitions[i] = obs[i].StableValue[sbIndex] != obs[i - 1].StableValue[sbIndex];
        int sbTransitionCount = sbTransitions.Count(t => t);

        var rows = new List<object?[]>();

        foreach (FusionDimension dim in allDims)
        {
            if (dim == FusionDimension.StructuralBreak) continue;
            int dimIndex = Array.IndexOf(allDims, dim);

            List<double> rawX = obs.Select(o => o.RawValue[dimIndex]).ToList();
            List<double> stableX = obs.Select(o => o.StableValue[dimIndex]).ToList();

            double pearsonRaw = AuditMath.Pearson(rawSb, rawX);
            double pearsonStable = AuditMath.Pearson(stableSb, stableX);
            double spearmanRaw = AuditMath.Spearman(rawSb, rawX);
            double spearmanStable = AuditMath.Spearman(stableSb, stableX);

            var xTransitions = new bool[n];
            for (int i = 1; i < n; i++) xTransitions[i] = obs[i].StableValue[dimIndex] != obs[i - 1].StableValue[dimIndex];
            int xTransitionCount = xTransitions.Count(t => t);

            int coTransitionCount = 0;
            for (int i = 1; i < n; i++)
                if (sbTransitions[i] && xTransitions[i]) coTransitionCount++;

            double? coPctOfSb = sbTransitionCount > 0 ? AuditMath.Pct(coTransitionCount, sbTransitionCount) : null;
            double? coPctOfX = xTransitionCount > 0 ? AuditMath.Pct(coTransitionCount, xTransitionCount) : null;

            rows.Add(new object?[]
            {
                dim.ToString(), n,
                Math.Round(pearsonRaw, 6), Math.Round(pearsonStable, 6),
                Math.Round(spearmanRaw, 6), Math.Round(spearmanStable, 6),
                sbTransitionCount, xTransitionCount, coTransitionCount,
                coPctOfSb, coPctOfX
            });
        }

        string outputDir = AuditCsv.ResolveOutputDirectory();
        AuditCsv.WriteCsv(Path.Combine(outputDir, "StructuralBreakRedundancy.csv"),
            dataset.Fingerprint,
            new[]
            {
                "OtherDimension", "N", "PearsonRaw", "PearsonStable", "SpearmanRaw", "SpearmanStable",
                "SBTransitionCount", "OtherTransitionCount", "CoTransitionCount",
                "CoTransitionPctOfSBTransitions", "CoTransitionPctOfOtherTransitions"
            },
            rows);

        _output.WriteLine("=== B.5: STRUCTURALBREAK REDUNDANCY VS OTHER 5 DIMENSIONS ===");
        _output.WriteLine($"N={n}, SBTransitionCount(StableValue)={sbTransitionCount}");
        _output.WriteLine("Dimension | N | PearsonRaw | PearsonStable | SpearmanRaw | SpearmanStable | SBTrans | OtherTrans | CoTrans | CoPct%OfSB | CoPct%OfOther");
        foreach (object?[] r in rows) _output.WriteLine(string.Join(" | ", r.Select(AuditCsv.FormatCell)));

        // NaN is a legitimate, documented outcome for a correlation over near/exactly-zero-variance input
        // (e.g. StructuralStability's RawValue is a constant 0.0 sentinel - see the builder's doc comment) -
        // exempt those 4 correlation columns (indices 2-5) from the no-NaN structural check.
        AuditCsv.AssertNoNaNOrInfinity(rows, (row, col) => col is 2 or 3 or 4 or 5);
        Assert.Equal(allDims.Length - 1, rows.Count);
    }
}
