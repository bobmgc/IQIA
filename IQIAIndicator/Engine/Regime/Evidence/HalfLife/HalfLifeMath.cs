namespace IQIAIndicator.Engine.Regime.Evidence.HalfLife;

/// <summary>
/// Fonctions numériques pures utilisées par l'estimation de la demi-vie.
/// </summary>
internal static class HalfLifeMath
{
    internal static double Sum(IReadOnlyList<double> values)
    {
        double sum = 0.0;
        for (int i = 0; i < values.Count; i++)
            sum += values[i];
        return sum;
    }

    internal static double Mean(IReadOnlyList<double> values) =>
        values.Count == 0 ? 0.0 : Sum(values) / values.Count;

    internal static double Variance(IReadOnlyList<double> values, double mean)
    {
        if (values.Count == 0)
            return 0.0;

        double sumSquaredDeviations = 0.0;
        for (int i = 0; i < values.Count; i++)
        {
            double deviation = values[i] - mean;
            sumSquaredDeviations += deviation * deviation;
        }

        return sumSquaredDeviations / values.Count;
    }

    internal static double Covariance(
        IReadOnlyList<double> first,
        double firstMean,
        IReadOnlyList<double> second,
        double secondMean)
    {
        if (first.Count == 0 || first.Count != second.Count)
            return 0.0;

        double sumProducts = 0.0;
        for (int i = 0; i < first.Count; i++)
            sumProducts += (first[i] - firstMean) * (second[i] - secondMean);

        return sumProducts / first.Count;
    }

    internal static double[] Differences(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
            return [];

        var differences = new double[values.Count - 1];
        for (int i = 1; i < values.Count; i++)
            differences[i - 1] = values[i] - values[i - 1];

        return differences;
    }
}