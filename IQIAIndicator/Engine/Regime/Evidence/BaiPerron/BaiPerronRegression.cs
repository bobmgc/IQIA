namespace IQIAIndicator.Engine.Regime.Evidence.BaiPerron;

/// <summary>
/// Régression OLS indépendante appliquée à un segment temporel.
/// </summary>
internal static class BaiPerronRegression
{
    private const double SingularTolerance = 1e-15;

    internal static bool TryFit(
        IReadOnlyList<double> values,
        int start,
        int end,
        out SegmentRegression result)
    {
        result = default;
        int sampleSize = end - start;
        if (start < 0 || end > values.Count || sampleSize < 2)
            return false;

        double sumX = 0.0;
        double sumY = 0.0;
        double sumXX = 0.0;
        double sumXY = 0.0;
        for (int i = start; i < end; i++)
        {
            double x = i;
            double y = values[i];
            if (!double.IsFinite(y))
                return false;

            sumX += x;
            sumY += y;
            sumXX += x * x;
            sumXY += x * y;
        }

        double denominator = sampleSize * sumXX - sumX * sumX;
        if (!double.IsFinite(denominator) || Math.Abs(denominator) <= SingularTolerance)
            return false;

        double slope = (sampleSize * sumXY - sumX * sumY) / denominator;
        double intercept = (sumY - slope * sumX) / sampleSize;
        double residualSumOfSquares = BaiPerronMath.ResidualSumOfSquares(values, start, end, intercept, slope);
        double mean = sumY / sampleSize;
        double totalSumOfSquares = 0.0;
        for (int i = start; i < end; i++)
        {
            double deviation = values[i] - mean;
            totalSumOfSquares += deviation * deviation;
        }

        double rSquared = totalSumOfSquares <= SingularTolerance
            ? 1.0
            : Math.Clamp(1.0 - residualSumOfSquares / totalSumOfSquares, 0.0, 1.0);
        if (!double.IsFinite(intercept) || !double.IsFinite(slope) ||
            !double.IsFinite(residualSumOfSquares) || !double.IsFinite(rSquared))
        {
            return false;
        }

        result = new SegmentRegression(intercept, slope, residualSumOfSquares, rSquared, sampleSize);
        return true;
    }

    internal readonly record struct SegmentRegression(
        double Intercept,
        double Slope,
        double ResidualSumOfSquares,
        double RSquared,
        int SampleSize);
}