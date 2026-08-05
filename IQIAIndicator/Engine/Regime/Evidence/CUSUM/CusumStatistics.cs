namespace IQIAIndicator.Engine.Regime.Evidence.CUSUM;

/// <summary>
/// Détecteur CUSUM de Page avec calibrage statistique sur le pré-échantillon.
/// </summary>
internal static class CusumStatistics
{
    private const double VarianceTolerance = 1e-15;

    internal static CusumResult Compute(IReadOnlyList<double> series)
    {
        if (series.Count < 8)
            return CusumResult.Invalid("Serie trop courte pour le CUSUM.", series.Count);

        for (int i = 0; i < series.Count; i++)
        {
            if (!double.IsFinite(series[i]))
                return CusumResult.Invalid("Serie non finie.", series.Count);
        }

        int calibrationSize = Math.Max(4, series.Count / 4);
        if (calibrationSize >= series.Count)
            return CusumResult.Invalid("Pre-echantillon CUSUM insuffisant.", series.Count);

        double referenceMean = CusumMath.Mean(series, calibrationSize);
        double referenceVariance = CusumMath.Variance(series, calibrationSize, referenceMean);
        if (!double.IsFinite(referenceVariance) || referenceVariance <= VarianceTolerance)
            return CusumResult.Invalid("Variance de reference nulle.", series.Count);

        double referenceStandardDeviation = Math.Sqrt(referenceVariance);
        CusumRun levelRun = RunPageCusum(
            series,
            calibrationSize,
            referenceMean,
            referenceStandardDeviation);

        double[] squaredDeviations = CusumMath.SquaredDeviations(series, referenceMean);
        double varianceOfSquaredDeviations = CusumMath.Variance(
            squaredDeviations,
            calibrationSize,
            referenceVariance);
        CusumRun varianceRun = varianceOfSquaredDeviations <= VarianceTolerance
            ? default
            : RunPageCusum(
                squaredDeviations,
                calibrationSize,
                referenceVariance,
                Math.Sqrt(varianceOfSquaredDeviations));

        CusumRun selectedRun = SelectMostSignificantRun(levelRun, varianceRun);
        double peakMagnitude = Math.Max(selectedRun.PositiveCusum, Math.Abs(selectedRun.NegativeCusum));
        double confidence = selectedRun.Threshold <= 0.0
            ? 0.0
            : Math.Clamp(peakMagnitude / selectedRun.Threshold, 0.0, 1.0);

        return new CusumResult
        {
            ChangeDetected = selectedRun.ChangeDetected,
            EstimatedBreakIndex = selectedRun.EstimatedBreakIndex,
            PositiveCusum = selectedRun.PositiveCusum,
            NegativeCusum = selectedRun.NegativeCusum,
            Threshold = selectedRun.Threshold,
            Confidence = confidence,
            SampleSize = series.Count,
            IsValid = true,
            Explanation = $"CUSUM {selectedRun.SeriesKind}: S+={selectedRun.PositiveCusum:F6}, " +
                $"S-={selectedRun.NegativeCusum:F6}, h={selectedRun.Threshold:F6}, " +
                $"k={selectedRun.ReferenceValue:F6}"
        };
    }

    private static CusumRun RunPageCusum(
        IReadOnlyList<double> series,
        int calibrationSize,
        double referenceMean,
        double standardDeviation)
    {
        int sampleSize = series.Count;
        double referenceValue = standardDeviation * Math.Sqrt(2.0 * Math.Log(sampleSize) / sampleSize) / 2.0;
        double threshold = standardDeviation * Math.Sqrt(2.0 * sampleSize * Math.Log(sampleSize));

        double positiveCusum = 0.0;
        double negativeCusum = 0.0;
        double maximumPositiveCusum = 0.0;
        double minimumNegativeCusum = 0.0;
        int positiveCandidateIndex = calibrationSize;
        int negativeCandidateIndex = calibrationSize;
        bool changeDetected = false;
        int estimatedBreakIndex = -1;

        for (int i = calibrationSize; i < sampleSize; i++)
        {
            double deviation = series[i] - referenceMean;
            positiveCusum = Math.Max(0.0, positiveCusum + deviation - referenceValue);
            negativeCusum = Math.Min(0.0, negativeCusum + deviation + referenceValue);

            if (positiveCusum == 0.0)
                positiveCandidateIndex = i + 1;
            if (negativeCusum == 0.0)
                negativeCandidateIndex = i + 1;

            maximumPositiveCusum = Math.Max(maximumPositiveCusum, positiveCusum);
            minimumNegativeCusum = Math.Min(minimumNegativeCusum, negativeCusum);

            if (!changeDetected && positiveCusum > threshold)
            {
                changeDetected = true;
                estimatedBreakIndex = positiveCandidateIndex;
            }
            else if (!changeDetected && Math.Abs(negativeCusum) > threshold)
            {
                changeDetected = true;
                estimatedBreakIndex = negativeCandidateIndex;
            }
        }

        return new CusumRun(
            changeDetected,
            estimatedBreakIndex,
            maximumPositiveCusum,
            minimumNegativeCusum,
            threshold,
            referenceValue,
            "niveau");
    }

    private static CusumRun SelectMostSignificantRun(CusumRun levelRun, CusumRun varianceRun)
    {
        double levelScore = levelRun.Threshold <= 0.0
            ? 0.0
            : Math.Max(levelRun.PositiveCusum, Math.Abs(levelRun.NegativeCusum)) / levelRun.Threshold;
        double varianceScore = varianceRun.Threshold <= 0.0
            ? 0.0
            : Math.Max(varianceRun.PositiveCusum, Math.Abs(varianceRun.NegativeCusum)) / varianceRun.Threshold;

        if (varianceScore <= levelScore)
            return levelRun;

        return varianceRun with { SeriesKind = "variance" };
    }

    private readonly record struct CusumRun(
        bool ChangeDetected,
        int EstimatedBreakIndex,
        double PositiveCusum,
        double NegativeCusum,
        double Threshold,
        double ReferenceValue,
        string SeriesKind);
}