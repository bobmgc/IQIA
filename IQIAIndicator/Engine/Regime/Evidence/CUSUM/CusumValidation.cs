namespace IQIAIndicator.Engine.Regime.Evidence.CUSUM;

/// <summary>
/// Comparaison avec une implementation Python de reference du CUSUM de Page.
/// </summary>
public static class CusumValidation
{
    /// <summary>
    /// Références calculées avec l'implémentation Python isomorphe du CUSUM de Page.
    /// </summary>
    public static IReadOnlyList<PythonReference> PythonReferences { get; } =
    [
        new("WhiteNoise", false, -1, 13.952213259737039, -7.508836521074523, -1),
        new("RandomWalk", true, 64, 896.435522467253236, -1132.472167906491222, -1),
        new("MeanShift", true, 126, 125.958011530864241, -3.431128493097786, 128),
        new("VarianceShift", true, 124, 352.635392946615411, -13.596845109577474, 128),
        new("OuProcess", false, -1, 75.621564001396052, -27.629149736854067, -1),
        new("TrendBreak", true, 136, 7003.038249231033660, -15.426806794948067, 128)
    ];

    public sealed record PythonReference(
        string SeriesName,
        bool ChangeDetected,
        int EstimatedBreakIndex,
        double PositiveCusum,
        double NegativeCusum,
        int KnownBreakIndex);

    public sealed record Metrics
    {
        public required string SeriesName { get; init; }
        public required bool DetectionMatch { get; init; }
        public required double PositiveCusumAbsoluteError { get; init; }
        public required double NegativeCusumAbsoluteError { get; init; }
        public required int PythonLocationAbsoluteError { get; init; }
        public required int KnownBreakAbsoluteError { get; init; }
        public required bool IsLocalizationComparable { get; init; }
        public required bool IsLocalizedWithinAdaptiveTolerance { get; init; }
        public required bool HasKnownBreak { get; init; }
        public required bool DetectedKnownBreak { get; init; }
    }

    public sealed record AggregateMetrics
    {
        public required double LocalizationMeanAbsoluteError { get; init; }
        public required double DetectionRate { get; init; }
        public required double LocalizationPrecision { get; init; }
    }

    public static CusumResult RunOnSeries(double[] series) => CusumStatistics.Compute(series);

    public static Metrics Compare(CusumResult result, PythonReference reference)
    {
        bool isLocalizationComparable = result.ChangeDetected && reference.ChangeDetected &&
            reference.KnownBreakIndex >= 0;
        int pythonLocationAbsoluteError = result.ChangeDetected && reference.ChangeDetected
            ? Math.Abs(result.EstimatedBreakIndex - reference.EstimatedBreakIndex)
            : 0;
        int knownBreakAbsoluteError = isLocalizationComparable
            ? Math.Abs(result.EstimatedBreakIndex - reference.KnownBreakIndex)
            : 0;
        int adaptiveTolerance = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(result.SampleSize)));

        return new Metrics
        {
            SeriesName = reference.SeriesName,
            DetectionMatch = result.IsValid && result.ChangeDetected == reference.ChangeDetected,
            PositiveCusumAbsoluteError = Math.Abs(result.PositiveCusum - reference.PositiveCusum),
            NegativeCusumAbsoluteError = Math.Abs(result.NegativeCusum - reference.NegativeCusum),
            PythonLocationAbsoluteError = pythonLocationAbsoluteError,
            KnownBreakAbsoluteError = knownBreakAbsoluteError,
            IsLocalizationComparable = isLocalizationComparable,
            IsLocalizedWithinAdaptiveTolerance = isLocalizationComparable &&
                knownBreakAbsoluteError <= adaptiveTolerance,
            HasKnownBreak = reference.KnownBreakIndex >= 0,
            DetectedKnownBreak = isLocalizationComparable
        };
    }

    public static AggregateMetrics Summarize(IReadOnlyList<Metrics> metrics)
    {
        int expectedDetectionCount = 0;
        int detectedExpectedBreakCount = 0;
        int localizationCount = 0;
        int localizedWithinToleranceCount = 0;
        double sumLocalizationErrors = 0.0;

        for (int i = 0; i < metrics.Count; i++)
        {
            if (metrics[i].IsLocalizationComparable)
            {
                localizationCount++;
                sumLocalizationErrors += metrics[i].KnownBreakAbsoluteError;
                if (metrics[i].IsLocalizedWithinAdaptiveTolerance)
                    localizedWithinToleranceCount++;
            }
        }

        for (int i = 0; i < metrics.Count; i++)
        {
            if (!metrics[i].HasKnownBreak)
                continue;

            expectedDetectionCount++;
            if (metrics[i].DetectionMatch && metrics[i].DetectedKnownBreak)
                detectedExpectedBreakCount++;
        }

        return new AggregateMetrics
        {
            LocalizationMeanAbsoluteError = localizationCount == 0 ? 0.0 : sumLocalizationErrors / localizationCount,
            DetectionRate = expectedDetectionCount == 0 ? 0.0 :
                (double)detectedExpectedBreakCount / expectedDetectionCount,
            LocalizationPrecision = localizationCount == 0 ? 0.0 :
                (double)localizedWithinToleranceCount / localizationCount
        };
    }

    public static IReadOnlyList<Metrics> CompareGoldenDatasets() =>
    [
        Compare(RunOnSeries(CusumGoldenDataset.WhiteNoise()), PythonReferences[0]),
        Compare(RunOnSeries(CusumGoldenDataset.RandomWalk()), PythonReferences[1]),
        Compare(RunOnSeries(CusumGoldenDataset.MeanShift()), PythonReferences[2]),
        Compare(RunOnSeries(CusumGoldenDataset.VarianceShift()), PythonReferences[3]),
        Compare(RunOnSeries(CusumGoldenDataset.OuProcess()), PythonReferences[4]),
        Compare(RunOnSeries(CusumGoldenDataset.TrendBreak()), PythonReferences[5])
    ];

    public static string RunAllReport()
    {
        CusumResult[] results =
        [
            RunOnSeries(CusumGoldenDataset.WhiteNoise()),
            RunOnSeries(CusumGoldenDataset.RandomWalk()),
            RunOnSeries(CusumGoldenDataset.MeanShift()),
            RunOnSeries(CusumGoldenDataset.VarianceShift()),
            RunOnSeries(CusumGoldenDataset.OuProcess()),
            RunOnSeries(CusumGoldenDataset.TrendBreak())
        ];

        var report = new System.Text.StringBuilder();
        report.AppendLine("=== Validation CUSUM IQIA vs Python ===");
        for (int i = 0; i < results.Length; i++)
        {
            CusumResult result = results[i];
            PythonReference reference = PythonReferences[i];
            int locationError = reference.KnownBreakIndex < 0 || !result.ChangeDetected
                ? 0
                : Math.Abs(result.EstimatedBreakIndex - reference.KnownBreakIndex);
            report.AppendLine($"{reference.SeriesName,-14} detecte={result.ChangeDetected,-5} " +
                $"indice={result.EstimatedBreakIndex,4} erreur={locationError,3} " +
                $"S+={result.PositiveCusum:F6} S-={result.NegativeCusum:F6}");
        }

        AggregateMetrics summary = Summarize(CompareGoldenDatasets());
        report.AppendLine($"MAE(localisation)={summary.LocalizationMeanAbsoluteError:F6}, " +
            $"taux_detection={summary.DetectionRate:P2}, precision_localisation={summary.LocalizationPrecision:P2}");
        return report.ToString();
    }
}