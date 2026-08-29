namespace IQIAIndicator.Engine.Regime.Evidence.BaiPerron;

/// <summary>
/// Outils de comparaison avec une segmentation de référence Python équivalente.
/// </summary>
public static class BaiPerronValidation
{
    /// <summary>
    /// Références Python issues de la programmation dynamique OLS et sélection BIC équivalentes.
    /// </summary>
    public static IReadOnlyList<PythonReference> PythonReferences { get; } =
    [
        new("WhiteNoise", [], 266.003414193553169, 20.903269969630948, []),
        new("RandomWalk", [16, 35, 54, 73, 94, 112, 140, 160, 184, 201, 225],
            235.811531650531322, 173.052204937146939, []),
        new("SingleMeanShift", [128], 265.853711771449582, 37.394689084689553, [128]),
        new("DoubleMeanShift", [85, 170], 262.723994104331382, 50.998625203967848, [85, 170]),
        new("TrendBreak", [145], 262.257113154338924, 33.907756276295132, [128]),
        new("TripleStructuralBreak", [64, 128, 192], 260.775785112611288,
            65.728736277795761, [64, 128, 192])
    ];

    public sealed record PythonReference(
        string SeriesName,
        IReadOnlyList<int> Breakpoints,
        double GlobalRSS,
        double BicScore,
        IReadOnlyList<int> KnownBreakpoints);

    public sealed record Metrics
    {
        public required string SeriesName { get; init; }
        public required bool BreakCountMatch { get; init; }
        public required double GlobalRssAbsoluteError { get; init; }
        public required double BicAbsoluteError { get; init; }
        public required double LocalizationMeanAbsoluteError { get; init; }
        public required int MatchedBreakpointCount { get; init; }
        public required int EstimatedBreakpointCount { get; init; }
        public required int KnownBreakpointCount { get; init; }
    }

    public sealed record AggregateMetrics
    {
        public required double LocalizationMeanAbsoluteError { get; init; }
        public required double DetectionRate { get; init; }
        public required double Precision { get; init; }
    }

    public static BaiPerronResult RunOnSeries(double[] series) => BaiPerronStatistics.Compute(series);

    public static Metrics Compare(BaiPerronResult result, PythonReference reference)
    {
        IReadOnlyList<int> estimatedBreakpoints = result.Breakpoints;
        double pythonLocalizationError = MeanAbsolutePositionError(
            estimatedBreakpoints,
            reference.Breakpoints);
        double knownLocalizationError = MeanAbsolutePositionError(
            estimatedBreakpoints,
            reference.KnownBreakpoints);
        int matchedBreakpointCount = CountMatchesWithinTolerance(
            estimatedBreakpoints,
            reference.KnownBreakpoints,
            Math.Max(1, (int)Math.Ceiling(Math.Sqrt(result.SampleSize))));

        return new Metrics
        {
            SeriesName = reference.SeriesName,
            BreakCountMatch = result.IsValid && estimatedBreakpoints.Count == reference.Breakpoints.Count,
            GlobalRssAbsoluteError = Math.Abs(result.GlobalRSS - reference.GlobalRSS),
            BicAbsoluteError = Math.Abs(result.BicScore - reference.BicScore),
            LocalizationMeanAbsoluteError = reference.KnownBreakpoints.Count == 0
                ? pythonLocalizationError
                : knownLocalizationError,
            MatchedBreakpointCount = matchedBreakpointCount,
            EstimatedBreakpointCount = estimatedBreakpoints.Count,
            KnownBreakpointCount = reference.KnownBreakpoints.Count
        };
    }

    public static AggregateMetrics Summarize(IReadOnlyList<Metrics> metrics)
    {
        double sumLocalizationErrors = 0.0;
        int localizationCount = 0;
        int matchedBreakpoints = 0;
        int estimatedBreakpoints = 0;
        int knownBreakpoints = 0;

        for (int i = 0; i < metrics.Count; i++)
        {
            estimatedBreakpoints += metrics[i].EstimatedBreakpointCount;
            if (metrics[i].KnownBreakpointCount > 0)
            {
                sumLocalizationErrors += metrics[i].LocalizationMeanAbsoluteError * metrics[i].KnownBreakpointCount;
                localizationCount += metrics[i].KnownBreakpointCount;
                matchedBreakpoints += metrics[i].MatchedBreakpointCount;
                knownBreakpoints += metrics[i].KnownBreakpointCount;
            }
        }

        return new AggregateMetrics
        {
            LocalizationMeanAbsoluteError = localizationCount == 0 ? 0.0 :
                sumLocalizationErrors / localizationCount,
            DetectionRate = knownBreakpoints == 0 ? 0.0 : (double)matchedBreakpoints / knownBreakpoints,
            Precision = estimatedBreakpoints == 0 ? 0.0 : (double)matchedBreakpoints / estimatedBreakpoints
        };
    }

    public static IReadOnlyList<Metrics> CompareGoldenDatasets() =>
    [
        Compare(RunOnSeries(BaiPerronGoldenDataset.WhiteNoise()), PythonReferences[0]),
        Compare(RunOnSeries(BaiPerronGoldenDataset.RandomWalk()), PythonReferences[1]),
        Compare(RunOnSeries(BaiPerronGoldenDataset.SingleMeanShift()), PythonReferences[2]),
        Compare(RunOnSeries(BaiPerronGoldenDataset.DoubleMeanShift()), PythonReferences[3]),
        Compare(RunOnSeries(BaiPerronGoldenDataset.TrendBreak()), PythonReferences[4]),
        Compare(RunOnSeries(BaiPerronGoldenDataset.TripleStructuralBreak()), PythonReferences[5])
    ];

    public static string RunAllReport()
    {
        BaiPerronResult[] results =
        [
            RunOnSeries(BaiPerronGoldenDataset.WhiteNoise()),
            RunOnSeries(BaiPerronGoldenDataset.RandomWalk()),
            RunOnSeries(BaiPerronGoldenDataset.SingleMeanShift()),
            RunOnSeries(BaiPerronGoldenDataset.DoubleMeanShift()),
            RunOnSeries(BaiPerronGoldenDataset.TrendBreak()),
            RunOnSeries(BaiPerronGoldenDataset.TripleStructuralBreak())
        ];

        var report = new System.Text.StringBuilder();
        report.AppendLine("=== Validation Bai-Perron IQIA vs Python ===");
        for (int i = 0; i < results.Length; i++)
        {
            BaiPerronResult result = results[i];
            PythonReference reference = PythonReferences[i];
            double locationError = reference.KnownBreakpoints.Count == 0
                ? 0.0
                : MeanAbsolutePositionError(result.Breakpoints, reference.KnownBreakpoints);
            report.AppendLine($"{reference.SeriesName,-22} ruptures=[{string.Join(",", result.Breakpoints)}] " +
                $"RSS={result.GlobalRSS:F6} BIC={result.BicScore:F6} erreur={locationError:F3}");
        }

        AggregateMetrics summary = Summarize(CompareGoldenDatasets());
        report.AppendLine($"MAE(localisation)={summary.LocalizationMeanAbsoluteError:F6}, " +
            $"taux_detection={summary.DetectionRate:P2}, precision={summary.Precision:P2}");
        return report.ToString();
    }

    private static double MeanAbsolutePositionError(
        IReadOnlyList<int> estimatedBreakpoints,
        IReadOnlyList<int> referenceBreakpoints)
    {
        int comparableCount = Math.Min(estimatedBreakpoints.Count, referenceBreakpoints.Count);
        if (comparableCount == 0)
            return 0.0;

        double sumAbsoluteErrors = 0.0;
        for (int i = 0; i < comparableCount; i++)
            sumAbsoluteErrors += Math.Abs(estimatedBreakpoints[i] - referenceBreakpoints[i]);
        return sumAbsoluteErrors / comparableCount;
    }

    private static int CountMatchesWithinTolerance(
        IReadOnlyList<int> estimatedBreakpoints,
        IReadOnlyList<int> knownBreakpoints,
        int tolerance)
    {
        int matchedCount = 0;
        int estimatedIndex = 0;
        int knownIndex = 0;
        while (estimatedIndex < estimatedBreakpoints.Count && knownIndex < knownBreakpoints.Count)
        {
            int difference = estimatedBreakpoints[estimatedIndex] - knownBreakpoints[knownIndex];
            if (Math.Abs(difference) <= tolerance)
            {
                matchedCount++;
                estimatedIndex++;
                knownIndex++;
            }
            else if (difference < 0)
            {
                estimatedIndex++;
            }
            else
            {
                knownIndex++;
            }
        }

        return matchedCount;
    }
}