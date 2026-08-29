namespace IQIAIndicator.Engine.Regime.Evidence.HalfLife;

/// <summary>
/// Estime la demi-vie de retour a la moyenne a partir de la regression OLS.
/// </summary>
internal static class HalfLifeStatistics
{
    private const double LambdaTolerance = 1e-12;

    internal static HalfLifeResult Compute(IReadOnlyList<double> series)
    {
        if (series.Count < 4)
            return HalfLifeResult.Invalid("Serie trop courte pour estimer la demi-vie.", series.Count);

        var laggedValues = new double[series.Count - 1];
        for (int i = 0; i < laggedValues.Length; i++)
        {
            if (!double.IsFinite(series[i]) || !double.IsFinite(series[i + 1]))
                return HalfLifeResult.Invalid("Serie non finie.", series.Count);

            laggedValues[i] = series[i];
        }

        double[] differences = HalfLifeMath.Differences(series);
        if (!HalfLifeRegression.TryFit(laggedValues, differences, out var regression))
            return HalfLifeResult.Invalid("Regression Half-Life invalide.", series.Count);

        if (regression.Lambda >= 0.0)
            return HalfLifeResult.Invalid("Lambda non negatif : absence de retour a la moyenne.", series.Count);

        if (Math.Abs(regression.Lambda) <= LambdaTolerance)
            return HalfLifeResult.Invalid("Lambda trop proche de zero.", series.Count);

        double halfLife = -Math.Log(2.0) / regression.Lambda;
        if (!double.IsFinite(halfLife) || halfLife <= 0.0)
            return HalfLifeResult.Invalid("Demi-vie non finie.", series.Count);

        return new HalfLifeResult
        {
            HalfLife = halfLife,
            Lambda = regression.Lambda,
            Intercept = regression.Intercept,
            StandardError = regression.StandardError,
            RSquared = regression.RSquared,
            Confidence = regression.RSquared,
            SampleSize = series.Count,
            IsValid = true,
            Explanation = $"lambda={regression.Lambda:F6}, HL={halfLife:F4}, R2={regression.RSquared:F4}"
        };
    }
}