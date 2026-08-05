namespace IQIAIndicator.Engine.Regime.Evidence.CUSUM;

/// <summary>
/// Fonctions numeriques pures utilisees par le detecteur CUSUM.
/// </summary>
internal static class CusumMath
{
    internal static double Mean(IReadOnlyList<double> values, int count)
    {
        if (count <= 0 || count > values.Count)
            return 0.0;

        double sum = 0.0;
        for (int i = 0; i < count; i++)
            sum += values[i];
        return sum / count;
    }

    internal static double Variance(IReadOnlyList<double> values, int count, double mean)
    {
        if (count < 2 || count > values.Count)
            return 0.0;

        double sumSquaredDeviations = 0.0;
        for (int i = 0; i < count; i++)
        {
            double deviation = values[i] - mean;
            sumSquaredDeviations += deviation * deviation;
        }

        return sumSquaredDeviations / (count - 1);
    }

    internal static double[] CumulativeSums(IReadOnlyList<double> values)
    {
        var cumulativeSums = new double[values.Count];
        double sum = 0.0;
        for (int i = 0; i < values.Count; i++)
        {
            sum += values[i];
            cumulativeSums[i] = sum;
        }

        return cumulativeSums;
    }

    internal static double[] SquaredDeviations(IReadOnlyList<double> values, double referenceMean)
    {
        var squaredDeviations = new double[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            double deviation = values[i] - referenceMean;
            squaredDeviations[i] = deviation * deviation;
        }

        return squaredDeviations;
    }
}