namespace IQIAIndicator.Engine.Regime.Evidence.HalfLife;

/// <summary>
/// Comparaison des resultats IQIA avec une reference Python scikit-learn.
/// </summary>
public static class HalfLifeValidation
{
    /// <summary>
    /// Références obtenues avec Python 3.13 et scikit-learn 1.9.0 sur les séries seed=42.
    /// </summary>
    public static IReadOnlyList<PythonReference> PythonReferences { get; } =
    [
        new("WhiteNoise", -0.983316602014278, 0.704907431787550, 0.491610105604410, true),
        new("RandomWalk", -0.041569858696510, 16.674273194441533, 0.021489673463675, true),
        new("OuProcess", -0.462206693737689, 1.499647646715647, 0.230833947806060, true),
        new("Ar1", -0.182423620741967, 3.799656961860118, 0.090456867736462, true),
        new("Trend", 0.010050160575824, -68.968766750587989, 0.999999803139705, false)
    ];

    public sealed record PythonReference(
        string SeriesName,
        double Lambda,
        double HalfLife,
        double RSquared,
        bool IsValid);

    public sealed record Metrics
    {
        public required string SeriesName { get; init; }
        public required bool ValidityMatch { get; init; }
        public required bool IsComparable { get; init; }
        public required double LambdaAbsoluteError { get; init; }
        public required double HalfLifeAbsoluteError { get; init; }
        public required double HalfLifeRelativeError { get; init; }
        public required double RSquaredAbsoluteError { get; init; }
    }

    public sealed record AggregateMetrics
    {
        public required double MeanAbsoluteError { get; init; }
        public required double RootMeanSquaredError { get; init; }
    }

    public static HalfLifeResult RunOnSeries(double[] series) => HalfLifeStatistics.Compute(series);

    public static Metrics Compare(HalfLifeResult result, PythonReference reference)
    {
        bool validityMatch = result.IsValid == reference.IsValid;
        if (!result.IsValid || !reference.IsValid)
        {
            return new Metrics
            {
                SeriesName = reference.SeriesName,
                ValidityMatch = validityMatch,
                IsComparable = false,
                LambdaAbsoluteError = 0.0,
                HalfLifeAbsoluteError = 0.0,
                HalfLifeRelativeError = 0.0,
                RSquaredAbsoluteError = 0.0
            };
        }

        double halfLifeAbsoluteError = Math.Abs(result.HalfLife - reference.HalfLife);
        return new Metrics
        {
            SeriesName = reference.SeriesName,
            ValidityMatch = validityMatch,
            IsComparable = true,
            LambdaAbsoluteError = Math.Abs(result.Lambda - reference.Lambda),
            HalfLifeAbsoluteError = halfLifeAbsoluteError,
            HalfLifeRelativeError = halfLifeAbsoluteError / Math.Max(Math.Abs(reference.HalfLife), 1e-12),
            RSquaredAbsoluteError = Math.Abs(result.RSquared - reference.RSquared)
        };
    }

    public static AggregateMetrics Summarize(IReadOnlyList<Metrics> metrics)
    {
        int comparableCount = 0;
        double sumAbsoluteErrors = 0.0;
        double sumSquaredErrors = 0.0;
        for (int i = 0; i < metrics.Count; i++)
        {
            if (!metrics[i].IsComparable)
                continue;

            double error = metrics[i].HalfLifeAbsoluteError;
            sumAbsoluteErrors += error;
            sumSquaredErrors += error * error;
            comparableCount++;
        }

        if (comparableCount == 0)
            return new AggregateMetrics { MeanAbsoluteError = 0.0, RootMeanSquaredError = 0.0 };

        return new AggregateMetrics
        {
            MeanAbsoluteError = sumAbsoluteErrors / comparableCount,
            RootMeanSquaredError = Math.Sqrt(sumSquaredErrors / comparableCount)
        };
    }

    public static IReadOnlyList<Metrics> CompareGoldenDatasets() =>
    [
        Compare(RunOnSeries(HalfLifeGoldenDataset.WhiteNoise()), PythonReferences[0]),
        Compare(RunOnSeries(HalfLifeGoldenDataset.RandomWalk()), PythonReferences[1]),
        Compare(RunOnSeries(HalfLifeGoldenDataset.OuProcess()), PythonReferences[2]),
        Compare(RunOnSeries(HalfLifeGoldenDataset.Ar1()), PythonReferences[3]),
        Compare(RunOnSeries(HalfLifeGoldenDataset.Trend()), PythonReferences[4])
    ];

    public static string RunAllReport()
    {
        HalfLifeResult[] results =
        [
            RunOnSeries(HalfLifeGoldenDataset.WhiteNoise()),
            RunOnSeries(HalfLifeGoldenDataset.RandomWalk()),
            RunOnSeries(HalfLifeGoldenDataset.OuProcess()),
            RunOnSeries(HalfLifeGoldenDataset.Ar1()),
            RunOnSeries(HalfLifeGoldenDataset.Trend())
        ];

        var report = new System.Text.StringBuilder();
        report.AppendLine("=== Validation Half-Life IQIA vs Python scikit-learn ===");
        for (int i = 0; i < results.Length; i++)
        {
            HalfLifeResult result = results[i];
            PythonReference reference = PythonReferences[i];
            report.AppendLine(result.IsValid
                ? $"{reference.SeriesName,-12} lambda={result.Lambda:F12} HL={result.HalfLife:F12} R2={result.RSquared:F12}"
                : $"{reference.SeriesName,-12} invalide: {result.Explanation}");
        }

        AggregateMetrics summary = Summarize(CompareGoldenDatasets());
        report.AppendLine($"MAE(HL)={summary.MeanAbsoluteError:E6}, RMSE(HL)={summary.RootMeanSquaredError:E6}");
        return report.ToString();
    }
}