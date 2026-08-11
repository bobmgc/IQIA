using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace IQIAIndicator.Core.Calibration;

public sealed record MetricStatistics(
    string Name,
    int Count,
    int Missing,
    double Minimum,
    double Maximum,
    double Mean,
    double Median,
    double Variance,
    double StandardDeviation,
    double FirstQuartile,
    double ThirdQuartile,
    int OutlierCount,
    int ExtremeCount);

public sealed record CorrelationResult(string Left, string Right, int Count, double Correlation, string Strength);

public sealed record MetricDistribution(
    string Name,
    int Count,
    int Missing,
    int ZeroCount,
    int ExtremeCount,
    IReadOnlyDictionary<string, int> Histogram);

public sealed class ScientificDatasetStatistics
{
    private ScientificDatasetStatistics(
        IReadOnlyList<MetricStatistics> metrics,
        IReadOnlyList<CorrelationResult> correlations,
        IReadOnlyList<MetricDistribution> distributions)
    {
        Metrics = metrics;
        Correlations = correlations;
        Distributions = distributions;
    }

    public IReadOnlyList<MetricStatistics> Metrics { get; }
    public IReadOnlyList<CorrelationResult> Correlations { get; }
    public IReadOnlyList<MetricDistribution> Distributions { get; }

    public static ScientificDatasetStatistics Calculate(IReadOnlyList<ScientificDatasetRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        string[] names = records.SelectMany(record => record.Metrics.Keys).Distinct(StringComparer.Ordinal).OrderBy(name => name).ToArray();
        var metricStats = new List<MetricStatistics>();
        var distributions = new List<MetricDistribution>();

        foreach (string name in names)
        {
            double[] values = records
                .Select(record => record.Metrics.TryGetValue(name, out double? value) ? value : null)
                .Where(value => value.HasValue && double.IsFinite(value.Value))
                .Select(value => value!.Value)
                .OrderBy(value => value)
                .ToArray();
            metricStats.Add(CalculateMetric(name, values, records.Count));
            distributions.Add(CalculateDistribution(name, values, records.Count));
        }

        var correlations = new List<CorrelationResult>();
        foreach ((string left, string right) in CorrelationPairs(names))
        {
            var pairs = records
                .Where(record => record.Metrics.TryGetValue(left, out double? leftValue) && leftValue.HasValue &&
                                 record.Metrics.TryGetValue(right, out double? rightValue) && rightValue.HasValue)
                .Select(record => (Left: record.Metrics[left]!.Value, Right: record.Metrics[right]!.Value))
                .Where(pair => double.IsFinite(pair.Left) && double.IsFinite(pair.Right))
                .ToArray();
            correlations.Add(CalculateCorrelation(left, right, pairs));
        }

        return new ScientificDatasetStatistics(
            new ReadOnlyCollection<MetricStatistics>(metricStats),
            new ReadOnlyCollection<CorrelationResult>(correlations),
            new ReadOnlyCollection<MetricDistribution>(distributions));
    }

    private static MetricStatistics CalculateMetric(string name, double[] values, int total)
    {
        if (values.Length == 0)
            return new MetricStatistics(name, 0, total, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, 0, 0);

        double mean = values.Average();
        double variance = values.Select(value => Math.Pow(value - mean, 2)).Average();
        double q1 = Percentile(values, 0.25);
        double median = Percentile(values, 0.50);
        double q3 = Percentile(values, 0.75);
        double iqr = q3 - q1;
        double lower = q1 - 1.5 * iqr;
        double upper = q3 + 1.5 * iqr;
        double extremeLower = q1 - 3.0 * iqr;
        double extremeUpper = q3 + 3.0 * iqr;

        return new MetricStatistics(
            name,
            values.Length,
            total - values.Length,
            values[0],
            values[^1],
            mean,
            median,
            variance,
            Math.Sqrt(variance),
            q1,
            q3,
            values.Count(value => value < lower || value > upper),
            values.Count(value => value < extremeLower || value > extremeUpper));
    }

    private static MetricDistribution CalculateDistribution(string name, double[] values, int total)
    {
        var histogram = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["(-inf,-2]"] = values.Count(value => value <= -2),
            ["(-2,-1]"] = values.Count(value => value > -2 && value <= -1),
            ["(-1,0)"] = values.Count(value => value > -1 && value < 0),
            ["0"] = values.Count(value => value == 0),
            ["(0,1]"] = values.Count(value => value > 0 && value <= 1),
            ["(1,2]"] = values.Count(value => value > 1 && value <= 2),
            ["(2,+inf)"] = values.Count(value => value > 2)
        };
        return new MetricDistribution(name, values.Length, total - values.Length, histogram["0"], 0, new ReadOnlyDictionary<string, int>(histogram));
    }

    private static CorrelationResult CalculateCorrelation(string left, string right, (double Left, double Right)[] pairs)
    {
        if (pairs.Length < 2)
            return new CorrelationResult(left, right, pairs.Length, double.NaN, "insufficient-data");

        double leftMean = pairs.Average(pair => pair.Left);
        double rightMean = pairs.Average(pair => pair.Right);
        double numerator = pairs.Sum(pair => (pair.Left - leftMean) * (pair.Right - rightMean));
        double leftSum = pairs.Sum(pair => Math.Pow(pair.Left - leftMean, 2));
        double rightSum = pairs.Sum(pair => Math.Pow(pair.Right - rightMean, 2));
        double denominator = Math.Sqrt(leftSum * rightSum);
        double correlation = denominator == 0.0 ? double.NaN : numerator / denominator;
        return new CorrelationResult(left, right, pairs.Length, correlation, Strength(correlation));
    }

    private static IEnumerable<(string Left, string Right)> CorrelationPairs(IReadOnlyCollection<string> names)
    {
        var requested = new[]
        {
            ("KalmanFilterModel.InnovationStd", "DynamicZScoreModel.DynamicZScore"),
            ("OrnsteinUhlenbeckModel.EstimatedTheta", "OrnsteinUhlenbeckModel.HalfLife"),
            ("VolatilityModel.RelativeVolatility", "VolatilityModel.VolatilityConfidence"),
            ("DynamicZScoreModel.DynamicConfidence", "SPRTModel.SPRTConfidence"),
            ("Fusion.OverallConfidence", "Decision.FinalScore")
        };
        return requested.Where(pair => names.Contains(pair.Item1) && names.Contains(pair.Item2));
    }

    private static string Strength(double value)
    {
        if (!double.IsFinite(value)) return "undefined";
        double magnitude = Math.Abs(value);
        return magnitude >= 0.8 ? "strong" : magnitude >= 0.5 ? "moderate" : "weak";
    }

    private static double Percentile(double[] sortedValues, double probability)
    {
        if (sortedValues.Length == 1) return sortedValues[0];
        double position = (sortedValues.Length - 1) * probability;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper) return sortedValues[lower];
        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * (position - lower);
    }
}
