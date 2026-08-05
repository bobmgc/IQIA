namespace IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;

/// <summary>
/// Test de Variance Ratio de Lo-MacKinlay avec variance asymptotique robuste.
/// </summary>
internal static class VarianceRatioStatistics
{
    private const double VarianceTolerance = 1e-15;

    internal static VarianceRatioResult Compute(IReadOnlyList<double> returns, int lag)
    {
        int sampleSize = returns.Count;
        if (lag < 2 || sampleSize <= lag)
            return VarianceRatioResult.Invalid("Echantillon insuffisant pour le lag demande.", lag, sampleSize);

        for (int i = 0; i < sampleSize; i++)
        {
            if (!double.IsFinite(returns[i]))
                return VarianceRatioResult.Invalid("Rendements non finis.", lag, sampleSize);
        }

        double mean = VarianceRatioMath.Mean(returns);
        double oneStepVariance = VarianceRatioMath.Variance(returns, mean);
        if (!double.IsFinite(oneStepVariance) || oneStepVariance <= VarianceTolerance)
            return VarianceRatioResult.Invalid("Variance a un pas nulle.", lag, sampleSize);

        int kPeriodCount = sampleSize - lag + 1;
        double sumSquaredKPeriodReturns = 0.0;
        for (int start = 0; start < kPeriodCount; start++)
        {
            double kPeriodReturn = 0.0;
            for (int offset = 0; offset < lag; offset++)
                kPeriodReturn += returns[start + offset];

            double centeredReturn = kPeriodReturn - lag * mean;
            sumSquaredKPeriodReturns += centeredReturn * centeredReturn;
        }

        double normalization = lag * kPeriodCount * (1.0 - (double)lag / sampleSize);
        double kStepVariance = sumSquaredKPeriodReturns / normalization;
        double varianceRatio = kStepVariance / oneStepVariance;
        if (!double.IsFinite(kStepVariance) || !double.IsFinite(varianceRatio))
            return VarianceRatioResult.Invalid("Variance Ratio non finie.", lag, sampleSize);

        double asymptoticVariance = ComputeRobustAsymptoticVariance(returns, mean, lag);
        if (!double.IsFinite(asymptoticVariance) || asymptoticVariance <= VarianceTolerance)
            return VarianceRatioResult.Invalid("Variance asymptotique nulle.", lag, sampleSize);

        double zStatistic = (varianceRatio - 1.0) / Math.Sqrt(asymptoticVariance);
        double pValue = TwoSidedNormalPValue(zStatistic);
        if (!double.IsFinite(zStatistic) || !double.IsFinite(pValue))
            return VarianceRatioResult.Invalid("Statistique de test non finie.", lag, sampleSize);

        return new VarianceRatioResult
        {
            VarianceRatio = varianceRatio,
            ZStatistic = zStatistic,
            PValue = pValue,
            Confidence = Math.Clamp(1.0 - pValue, 0.0, 1.0),
            Lag = lag,
            SampleSize = sampleSize,
            IsValid = true,
            Explanation = $"VR({lag})={varianceRatio:F6}, Z={zStatistic:F6}, p={pValue:F6}"
        };
    }

    private static double ComputeRobustAsymptoticVariance(
        IReadOnlyList<double> returns,
        double mean,
        int lag)
    {
        double sumSquaredDeviations = 0.0;
        for (int i = 0; i < returns.Count; i++)
        {
            double deviation = returns[i] - mean;
            sumSquaredDeviations += deviation * deviation;
        }

        double denominator = sumSquaredDeviations * sumSquaredDeviations;
        if (!double.IsFinite(denominator) || denominator <= VarianceTolerance)
            return 0.0;

        double variance = 0.0;
        for (int autocovarianceLag = 1; autocovarianceLag < lag; autocovarianceLag++)
        {
            double sumProducts = 0.0;
            for (int i = autocovarianceLag; i < returns.Count; i++)
            {
                double currentDeviation = returns[i] - mean;
                double laggedDeviation = returns[i - autocovarianceLag] - mean;
                sumProducts += currentDeviation * currentDeviation * laggedDeviation * laggedDeviation;
            }

            double delta = sumProducts / denominator;
            double weight = 2.0 * (lag - autocovarianceLag) / lag;
            variance += weight * weight * delta;
        }

        return variance;
    }

    private static double TwoSidedNormalPValue(double zStatistic)
    {
        double cumulativeProbability = NormalCdf(Math.Abs(zStatistic));
        return Math.Clamp(2.0 * (1.0 - cumulativeProbability), 0.0, 1.0);
    }

    private static double NormalCdf(double value)
    {
        double absoluteValue = Math.Abs(value);
        double t = 1.0 / (1.0 + 0.2316419 * absoluteValue);
        double polynomial = t * (0.319381530 + t * (-0.356563782 +
            t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
        double density = Math.Exp(-0.5 * absoluteValue * absoluteValue) / Math.Sqrt(2.0 * Math.PI);
        double positiveCdf = 1.0 - density * polynomial;
        return value < 0.0 ? 1.0 - positiveCdf : positiveCdf;
    }
}