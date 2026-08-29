using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Tests.Research.StructuralBreakInformationAudit.Support;
using Xunit;

namespace IQIAIndicator.Tests.Research.StructuralBreakInformationAudit;

/// <summary>
/// Lot 15.9, brief Sections B.3 (Bai-Perron BreakCount distribution) and B.4 (CUSUM x Bai-Perron agreement
/// matrix, using <see cref="StructuralBreakContract.Agreement"/> as-is - no new agreement definition
/// invented). AUDIT-ONLY.
/// </summary>
public sealed class StructuralBreakCusumBaiPerronAgreementAuditTests
{
    private readonly ITestOutputHelper _output;

    public StructuralBreakCusumBaiPerronAgreementAuditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ═══════════════════════════════════ B.3: BaiPerron.BreakCount distribution ═══════════════════════════

    [Fact]
    public void B3_BaiPerronBreakCountDistribution_Histogram_ChangeFrequency_Plateaus()
    {
        AuditDataset? dataset = StructuralBreakObservationSetBuilder.Instance;
        if (dataset is null)
        {
            Assert.Skip("Yahoo provider unavailable (not a code failure).");
        }

        List<ObservationRecord> validBars = dataset.Observations.Where(o => o.BaiPerron is { IsValid: true }).ToList();
        List<int> breakCounts = validBars.Select(o => o.BaiPerron!.BreakCount).ToList();
        List<double> asDouble = breakCounts.Select(v => (double)v).ToList();

        var summaryRow = breakCounts.Count == 0
            ? new object?[] { "SUMMARY", 0, null, null, null, null, null, 0 }
            : new object?[]
            {
                "SUMMARY", breakCounts.Count, breakCounts.Min(), breakCounts.Max(),
                Math.Round(asDouble.Average(), 6),
                Math.Round(AuditMath.Percentile(asDouble.OrderBy(v => v).ToList(), 0.5), 6),
                Math.Round(AuditMath.Variance(asDouble), 6),
                breakCounts.Distinct().Count()
            };

        var histogram = breakCounts.GroupBy(v => v).OrderBy(g => g.Key)
            .Select(g => new object?[] { "HISTOGRAM", g.Key, g.Count(), AuditMath.Pct(g.Count(), breakCounts.Count), null, null, null, null })
            .ToList();

        int changeCount = 0;
        for (int i = 1; i < breakCounts.Count; i++)
            if (breakCounts[i] != breakCounts[i - 1]) changeCount++;
        int consecutivePairs = Math.Max(0, breakCounts.Count - 1);

        List<PlateauRun<int>> plateaus = AuditMath.ComputePlateaus(breakCounts);
        List<double> plateauLengths = plateaus.Select(p => (double)p.Length).ToList();

        var changeRow = new object?[] { "CHANGE_FREQUENCY", changeCount, AuditMath.Pct(changeCount, consecutivePairs), consecutivePairs, null, null, null, null };
        var plateauRow = plateauLengths.Count == 0
            ? new object?[] { "PLATEAUS", 0, null, null, null, null, null, null }
            : new object?[]
            {
                "PLATEAUS", plateaus.Count,
                Math.Round(plateauLengths.Average(), 6),
                Math.Round(AuditMath.Percentile(plateauLengths.OrderBy(v => v).ToList(), 0.5), 6),
                plateauLengths.Max(), null, null, null
            };

        string outputDir = AuditCsv.ResolveOutputDirectory();
        var allRows = new List<object?[]> { summaryRow };
        allRows.AddRange(histogram);
        allRows.Add(changeRow);
        allRows.Add(plateauRow);

        AuditCsv.WriteCsv(Path.Combine(outputDir, "BaiPerronBreakCountDistribution.csv"),
            dataset.Fingerprint,
            new[] { "RowType", "Col1", "Col2", "Col3", "Col4", "Col5", "Col6", "Col7" },
            allRows);

        _output.WriteLine("=== B.3: BAIPERRON.BREAKCOUNT DISTRIBUTION (BaiPerron.IsValid==true) ===");
        _output.WriteLine("RowType | N/Value | Min/Count | Max/Pct | Mean/... | Median | Variance | DistinctOrMaxRun");
        foreach (object?[] r in allRows) _output.WriteLine(string.Join(" | ", r.Select(AuditCsv.FormatCell)));

        AuditCsv.AssertNoNaNOrInfinity(allRows);
        Assert.Equal(100.0, Math.Round(histogram.Sum(r => (double)r[3]!), 1));
    }

    // ═══════════════════════════════════ B.4: CUSUM x Bai-Perron agreement matrix ═════════════════════════

    [Fact]
    public void B4_CusumBaiPerronAgreementMatrix_UsingContractAgreement_TransitionsAndDurations()
    {
        AuditDataset? dataset = StructuralBreakObservationSetBuilder.Instance;
        if (dataset is null)
        {
            Assert.Skip("Yahoo provider unavailable (not a code failure).");
        }

        IReadOnlyList<ObservationRecord> obs = dataset.Observations;
        int n = obs.Count;
        List<StructuralBreakAgreement> agreements = obs.Select(o => o.Contract.Agreement).ToList();

        var countRows = Enum.GetValues<StructuralBreakAgreement>()
            .Select(a => new object?[] { "COUNT", a.ToString(), agreements.Count(x => x == a), AuditMath.Pct(agreements.Count(x => x == a), n) })
            .ToList();

        int transitionCount = 0;
        for (int i = 1; i < n; i++)
            if (agreements[i] != agreements[i - 1]) transitionCount++;
        int consecutivePairs = Math.Max(0, n - 1);

        List<PlateauRun<StructuralBreakAgreement>> plateaus = AuditMath.ComputePlateaus(agreements);
        List<double> durations = plateaus.Select(p => (double)p.Length).ToList();

        var transitionRow = new object?[] { "TRANSITIONS", "AgreementCategoryChanges", transitionCount, AuditMath.Pct(transitionCount, consecutivePairs) };
        var durationRow = new object?[]
        {
            "CATEGORY_DURATION", "PlateauLengths",
            Math.Round(durations.Average(), 6),
            Math.Round(AuditMath.Percentile(durations.OrderBy(v => v).ToList(), 0.5), 6)
        };
        var durationCountRow = new object?[] { "CATEGORY_DURATION_META", "PlateauCount", plateaus.Count, durations.Max() };

        string outputDir = AuditCsv.ResolveOutputDirectory();
        var allRows = new List<object?[]>();
        allRows.AddRange(countRows);
        allRows.Add(transitionRow);
        allRows.Add(durationRow);
        allRows.Add(durationCountRow);

        AuditCsv.WriteCsv(Path.Combine(outputDir, "CusumBaiPerronAgreement.csv"),
            dataset.Fingerprint,
            new[] { "RowType", "Label", "Count", "PctOrMedian" },
            allRows);

        _output.WriteLine("=== B.4: CUSUM x BAI-PERRON AGREEMENT (Contract.Agreement as-is) ===");
        _output.WriteLine("RowType | Label | Count | Pct%/Median");
        foreach (object?[] r in allRows) _output.WriteLine(string.Join(" | ", r.Select(AuditCsv.FormatCell)));
        _output.WriteLine($"MeanCategoryDuration={durations.Average():F3} bars, MedianCategoryDuration={AuditMath.Percentile(durations.OrderBy(v => v).ToList(), 0.5):F3} bars, MaxCategoryDuration={durations.Max()} bars, PlateauCount={plateaus.Count}");

        AuditCsv.AssertNoNaNOrInfinity(allRows);
        double sumPct = countRows.Sum(r => (double)r[3]!);
        Assert.Equal(100.0, Math.Round(sumPct, 1));
    }
}
