namespace IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;

/// <summary>
/// Fonctions numeriques pures utilisees par le test de Variance Ratio.
/// </summary>
internal static class VarianceRatioMath
{
    internal static double Mean(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0.0;

        double sum = 0.0;
        for (int i = 0; i < values.Count; i++)
            sum += values[i];
        return sum / values.Count;
    }

    internal static double Variance(IReadOnlyList<double> values, double mean)
    {
        if (values.Count < 2)
            return 0.0;

        double sumSquaredDeviations = 0.0;
        for (int i = 0; i < values.Count; i++)
        {
            double deviation = values[i] - mean;
            sumSquaredDeviations += deviation * deviation;
        }

        return sumSquaredDeviations / (values.Count - 1);
    }

    internal static double Covariance(
        IReadOnlyList<double> first,
        double firstMean,
        IReadOnlyList<double> second,
        double secondMean)
    {
        if (first.Count < 2 || first.Count != second.Count)
            return 0.0;

        double sumProducts = 0.0;
        for (int i = 0; i < first.Count; i++)
            sumProducts += (first[i] - firstMean) * (second[i] - secondMean);

        return sumProducts / (first.Count - 1);
    }

    internal static double AutoCovariance(IReadOnlyList<double> values, double mean, int lag)
    {
        if (lag <= 0 || lag >= values.Count)
            return 0.0;

        double sumProducts = 0.0;
        for (int i = lag; i < values.Count; i++)
            sumProducts += (values[i] - mean) * (values[i - lag] - mean);

        return sumProducts / (values.Count - lag);
    }

    internal static bool TryLogReturns(IReadOnlyList<double> prices, out double[] returns)
    {
        returns = [];
        if (prices.Count < 2)
            return false;

        returns = new double[prices.Count - 1];
        for (int i = 1; i < prices.Count; i++)
        {
            double previousPrice = prices[i - 1];
            double currentPrice = prices[i];
            if (!double.IsFinite(previousPrice) || !double.IsFinite(currentPrice) ||
                previousPrice <= 0.0 || currentPrice <= 0.0)
            {
                returns = [];
                return false;
            }

            double value = Math.Log(currentPrice / previousPrice);
            if (!double.IsFinite(value))
            {
                returns = [];
                return false;
            }

            returns[i - 1] = value;
        }

        return true;
    }
}