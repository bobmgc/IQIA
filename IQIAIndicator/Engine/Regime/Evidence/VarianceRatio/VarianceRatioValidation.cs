namespace IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;

/// <summary>
/// Outils de comparaison avec une implementation de reference NumPy.
/// </summary>
public static class VarianceRatioValidation
{
    /// <summary>
    /// Références obtenues avec NumPy sur les séries déterministes seed=42 au lag 5.
    /// </summary>
    public static IReadOnlyList<PythonReference> PythonReferences { get; } =
    [
        new("WhiteNoise", 1.165509225640102, 1.146006997466974, 0.251792256742018),
        new("RandomWalk", 1.166127707318382, 1.149682718502628, 0.250274574651177),
        new("PositiveAr1", 2.907118405589407, 11.302952334616846, 0.0),
        new("NegativeAr1", 0.397215891093089, -3.574305560004737, 0.000351158653530),
        new("OuProcess", 0.802466406444700, -1.364845557164775, 0.172301596688283),
        new("Trend", 1.165509225640095, 1.146006997466923, 0.251792256742039)
    ];

    public sealed record PythonReference(
        string SeriesName,
        double VarianceRatio,
        double ZStatistic,
        double PValue);

    public sealed record Metrics
    {
        public required string SeriesName { get; init; }
        public required double VarianceRatioAbsoluteError { get; init; }
        public required double VarianceRatioRelativeError { get; init; }
        public required double ZStatisticAbsoluteError { get; init; }
        public required double PValueAbsoluteError { get; init; }
        public required double ComputedVarianceRatio { get; init; }
        public required double ReferenceVarianceRatio { get; init; }
    }

    public sealed record AggregateMetrics
    {
        public required double MeanAbsoluteError { get; init; }
        public required double RootMeanSquaredError { get; init; }
        public required double MeanRelativeError { get; init; }
        public required double Correlation { get; init; }
    }

    public static VarianceRatioResult RunOnPrices(double[] prices, int lag = 5)
    {
        if (!VarianceRatioMath.TryLogReturns(prices, out double[] returns))
            return VarianceRatioResult.Invalid("Prix invalides pour les rendements logarithmiques.", lag, prices.Length);

        return VarianceRatioStatistics.Compute(returns, lag);
    }

    public static Metrics Compare(VarianceRatioResult result, PythonReference reference)
    {
        if (!result.IsValid)
        {
            return new Metrics
            {
                SeriesName = reference.SeriesName,
                VarianceRatioAbsoluteError = double.MaxValue,
                VarianceRatioRelativeError = double.MaxValue,
                ZStatisticAbsoluteError = double.MaxValue,
                PValueAbsoluteError = double.MaxValue,
                ComputedVarianceRatio = 0.0,
                ReferenceVarianceRatio = reference.VarianceRatio
            };
        }

        double varianceRatioAbsoluteError = Math.Abs(result.VarianceRatio - reference.VarianceRatio);
        return new Metrics
        {
            SeriesName = reference.SeriesName,
            VarianceRatioAbsoluteError = varianceRatioAbsoluteError,
            VarianceRatioRelativeError = varianceRatioAbsoluteError /
                Math.Max(Math.Abs(reference.VarianceRatio), 1e-12),
            ZStatisticAbsoluteError = Math.Abs(result.ZStatistic - reference.ZStatistic),
            PValueAbsoluteError = Math.Abs(result.PValue - reference.PValue),
            ComputedVarianceRatio = result.VarianceRatio,
            ReferenceVarianceRatio = reference.VarianceRatio
        };
    }

    public static AggregateMetrics Summarize(IReadOnlyList<Metrics> metrics)
    {
        if (metrics.Count == 0)
        {
            return new AggregateMetrics
            {
                MeanAbsoluteError = 0.0,
                RootMeanSquaredError = 0.0,
                MeanRelativeError = 0.0,
                Correlation = 0.0
            };
        }

        double sumAbsoluteErrors = 0.0;
        double sumSquaredErrors = 0.0;
        double sumRelativeErrors = 0.0;
        double computedMean = 0.0;
        double referenceMean = 0.0;
        for (int i = 0; i < metrics.Count; i++)
        {
            sumAbsoluteErrors += metrics[i].VarianceRatioAbsoluteError;
            sumSquaredErrors += metrics[i].VarianceRatioAbsoluteError * metrics[i].VarianceRatioAbsoluteError;
            sumRelativeErrors += metrics[i].VarianceRatioRelativeError;
            computedMean += metrics[i].ComputedVarianceRatio;
            referenceMean += metrics[i].ReferenceVarianceRatio;
        }

        computedMean /= metrics.Count;
        referenceMean /= metrics.Count;

        double covariance = 0.0;
        double computedVariance = 0.0;
        double referenceVariance = 0.0;
        for (int i = 0; i < metrics.Count; i++)
        {
            double computedDeviation = metrics[i].ComputedVarianceRatio - computedMean;
            double referenceDeviation = metrics[i].ReferenceVarianceRatio - referenceMean;
            covariance += computedDeviation * referenceDeviation;
            computedVariance += computedDeviation * computedDeviation;
            referenceVariance += referenceDeviation * referenceDeviation;
        }

        double correlation = computedVariance <= 0.0 || referenceVariance <= 0.0
            ? 0.0
            : covariance / Math.Sqrt(computedVariance * referenceVariance);

        return new AggregateMetrics
        {
            MeanAbsoluteError = sumAbsoluteErrors / metrics.Count,
            RootMeanSquaredError = Math.Sqrt(sumSquaredErrors / metrics.Count),
            MeanRelativeError = sumRelativeErrors / metrics.Count,
            Correlation = correlation
        };
    }

    public static IReadOnlyList<Metrics> CompareGoldenDatasets() =>
    [
        Compare(RunOnPrices(VarianceRatioGoldenDataset.WhiteNoise()), PythonReferences[0]),
        Compare(RunOnPrices(VarianceRatioGoldenDataset.RandomWalk()), PythonReferences[1]),
        Compare(RunOnPrices(VarianceRatioGoldenDataset.PositiveAr1()), PythonReferences[2]),
        Compare(RunOnPrices(VarianceRatioGoldenDataset.NegativeAr1()), PythonReferences[3]),
        Compare(RunOnPrices(VarianceRatioGoldenDataset.OuProcess()), PythonReferences[4]),
        Compare(RunOnPrices(VarianceRatioGoldenDataset.Trend()), PythonReferences[5])
    ];

    public static string RunAllReport()
    {
        VarianceRatioResult[] results =
        [
            RunOnPrices(VarianceRatioGoldenDataset.WhiteNoise()),
            RunOnPrices(VarianceRatioGoldenDataset.RandomWalk()),
            RunOnPrices(VarianceRatioGoldenDataset.PositiveAr1()),
            RunOnPrices(VarianceRatioGoldenDataset.NegativeAr1()),
            RunOnPrices(VarianceRatioGoldenDataset.OuProcess()),
            RunOnPrices(VarianceRatioGoldenDataset.Trend())
        ];

        var report = new System.Text.StringBuilder();
        report.AppendLine("=== Validation Variance Ratio IQIA vs NumPy ===");
        for (int i = 0; i < results.Length; i++)
        {
            VarianceRatioResult result = results[i];
            report.AppendLine(result.IsValid
                ? $"{PythonReferences[i].SeriesName,-12} VR={result.VarianceRatio:F12} Z={result.ZStatistic:F12} p={result.PValue:F12}"
                : $"{PythonReferences[i].SeriesName,-12} invalide: {result.Explanation}");
        }

        AggregateMetrics summary = Summarize(CompareGoldenDatasets());
        report.AppendLine($"MAE(VR)={summary.MeanAbsoluteError:E6}, RMSE(VR)={summary.RootMeanSquaredError:E6}, " +
            $"MRE(VR)={summary.MeanRelativeError:E6}, correlation={summary.Correlation:F12}");
        return report.ToString();
    }
}