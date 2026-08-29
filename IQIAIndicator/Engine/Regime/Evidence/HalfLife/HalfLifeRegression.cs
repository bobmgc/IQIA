namespace IQIAIndicator.Engine.Regime.Evidence.HalfLife;

/// <summary>
/// Régression MCO générique : y = intercept + slope * x + erreur.
/// </summary>
internal static class HalfLifeRegression
{
    private const double SingularTolerance = 1e-12;

    internal static bool TryFit(
        IReadOnlyList<double> x,
        IReadOnlyList<double> y,
        out RegressionResult result)
    {
        result = default;
        if (x.Count != y.Count || x.Count < 3)
            return false;

        double xMean = HalfLifeMath.Mean(x);
        double yMean = HalfLifeMath.Mean(y);
        double varianceX = HalfLifeMath.Variance(x, xMean);
        if (!double.IsFinite(varianceX) || varianceX <= SingularTolerance)
            return false;

        double covariance = HalfLifeMath.Covariance(x, xMean, y, yMean);
        double lambda = covariance / varianceX;
        double intercept = yMean - lambda * xMean;
        if (!double.IsFinite(lambda) || !double.IsFinite(intercept))
            return false;

        double sumSquaredResiduals = 0.0;
        double totalSumSquares = 0.0;
        double sumSquaredXDeviations = 0.0;
        for (int i = 0; i < x.Count; i++)
        {
            double residual = y[i] - (intercept + lambda * x[i]);
            double yDeviation = y[i] - yMean;
            double xDeviation = x[i] - xMean;
            sumSquaredResiduals += residual * residual;
            totalSumSquares += yDeviation * yDeviation;
            sumSquaredXDeviations += xDeviation * xDeviation;
        }

        if (!double.IsFinite(sumSquaredResiduals) || !double.IsFinite(totalSumSquares))
            return false;

        int degreesOfFreedom = x.Count - 2;
        double standardError = Math.Sqrt(sumSquaredResiduals / degreesOfFreedom / sumSquaredXDeviations);
        double rSquared = totalSumSquares <= SingularTolerance
            ? 0.0
            : Math.Clamp(1.0 - sumSquaredResiduals / totalSumSquares, 0.0, 1.0);

        if (!double.IsFinite(standardError) || !double.IsFinite(rSquared))
            return false;

        result = new RegressionResult(lambda, intercept, standardError, rSquared, x.Count);
        return true;
    }

    internal readonly record struct RegressionResult(
        double Lambda,
        double Intercept,
        double StandardError,
        double RSquared,
        int SampleSize);
}