namespace IQIAIndicator.Engine.Regime.Evidence.BaiPerron;

/// <summary>
/// Fonctions numeriques pures pour la segmentation par ruptures structurelles.
/// </summary>
internal static class BaiPerronMath
{
    internal static double Mean(IReadOnlyList<double> values, int start, int end)
    {
        if (start < 0 || end > values.Count || start >= end)
            return 0.0;

        double sum = 0.0;
        for (int i = start; i < end; i++)
            sum += values[i];
        return sum / (end - start);
    }

    internal static double Variance(IReadOnlyList<double> values, int start, int end, double mean)
    {
        if (start < 0 || end > values.Count || end - start < 2)
            return 0.0;

        double sumSquaredDeviations = 0.0;
        for (int i = start; i < end; i++)
        {
            double deviation = values[i] - mean;
            sumSquaredDeviations += deviation * deviation;
        }

        return sumSquaredDeviations / (end - start - 1);
    }

    internal static double[] CumulativeSums(IReadOnlyList<double> values)
    {
        var cumulativeSums = new double[values.Count + 1];
        for (int i = 0; i < values.Count; i++)
            cumulativeSums[i + 1] = cumulativeSums[i] + values[i];
        return cumulativeSums;
    }

    internal static double Sum(IReadOnlyList<double> cumulativeSums, int start, int end) =>
        cumulativeSums[end] - cumulativeSums[start];

    internal static double ResidualSumOfSquares(
        IReadOnlyList<double> values,
        int start,
        int end,
        double intercept,
        double slope)
    {
        double residualSumOfSquares = 0.0;
        for (int i = start; i < end; i++)
        {
            double residual = values[i] - (intercept + slope * i);
            residualSumOfSquares += residual * residual;
        }

        return residualSumOfSquares;
    }
}