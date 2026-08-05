namespace IQIAIndicator.Engine.Regime.Evidence.BaiPerron;

/// <summary>
/// Segmentation multiple par programmation dynamique avec sélection BIC.
/// </summary>
internal static class BaiPerronStatistics
{
    private const double RssTolerance = 1e-15;

    internal static BaiPerronResult Compute(IReadOnlyList<double> series)
    {
        int sampleSize = series.Count;
        if (sampleSize < 6)
            return BaiPerronResult.Invalid("Serie trop courte pour la segmentation.", sampleSize);

        for (int i = 0; i < sampleSize; i++)
        {
            if (!double.IsFinite(series[i]))
                return BaiPerronResult.Invalid("Serie non finie.", sampleSize);
        }

        int minimumSegmentSize = Math.Max(3, (int)Math.Ceiling(Math.Sqrt(sampleSize)));
        int maximumSegmentCount = sampleSize / minimumSegmentSize;
        if (maximumSegmentCount == 0)
            return BaiPerronResult.Invalid("Taille de segment minimale invalide.", sampleSize);

        var segmentRss = new double[sampleSize + 1, sampleSize + 1];
        for (int start = 0; start < sampleSize; start++)
        {
            for (int end = start + minimumSegmentSize; end <= sampleSize; end++)
            {
                if (BaiPerronRegression.TryFit(series, start, end, out var regression))
                    segmentRss[start, end] = regression.ResidualSumOfSquares;
                else
                    segmentRss[start, end] = double.PositiveInfinity;
            }
        }

        var dynamicRss = new double[maximumSegmentCount + 1, sampleSize + 1];
        var previousBreak = new int[maximumSegmentCount + 1, sampleSize + 1];
        for (int segmentCount = 0; segmentCount <= maximumSegmentCount; segmentCount++)
        {
            for (int end = 0; end <= sampleSize; end++)
            {
                dynamicRss[segmentCount, end] = double.PositiveInfinity;
                previousBreak[segmentCount, end] = -1;
            }
        }

        dynamicRss[0, 0] = 0.0;
        for (int segmentCount = 1; segmentCount <= maximumSegmentCount; segmentCount++)
        {
            int minimumEnd = segmentCount * minimumSegmentSize;
            for (int end = minimumEnd; end <= sampleSize; end++)
            {
                int minimumStart = (segmentCount - 1) * minimumSegmentSize;
                int maximumStart = end - minimumSegmentSize;
                for (int start = minimumStart; start <= maximumStart; start++)
                {
                    double precedingRss = dynamicRss[segmentCount - 1, start];
                    double currentRss = segmentRss[start, end];
                    if (!double.IsFinite(precedingRss) || !double.IsFinite(currentRss))
                        continue;

                    double candidateRss = precedingRss + currentRss;
                    if (candidateRss < dynamicRss[segmentCount, end])
                    {
                        dynamicRss[segmentCount, end] = candidateRss;
                        previousBreak[segmentCount, end] = start;
                    }
                }
            }
        }

        int bestSegmentCount = 0;
        double bestBic = double.PositiveInfinity;
        double baselineBic = double.PositiveInfinity;
        for (int segmentCount = 1; segmentCount <= maximumSegmentCount; segmentCount++)
        {
            double globalRss = dynamicRss[segmentCount, sampleSize];
            if (!double.IsFinite(globalRss))
                continue;

            int parameterCount = 3 * segmentCount - 1;
            double bic = sampleSize * Math.Log(Math.Max(globalRss / sampleSize, RssTolerance)) +
                parameterCount * Math.Log(sampleSize);
            if (segmentCount == 1)
                baselineBic = bic;
            if (bic < bestBic)
            {
                bestBic = bic;
                bestSegmentCount = segmentCount;
            }
        }

        if (bestSegmentCount == 0)
            return BaiPerronResult.Invalid("Aucune segmentation OLS valide.", sampleSize);

        IReadOnlyList<int> breakpoints = ReconstructBreakpoints(
            previousBreak,
            bestSegmentCount,
            sampleSize);
        double bestRss = dynamicRss[bestSegmentCount, sampleSize];
        double bicImprovement = Math.Max(0.0, baselineBic - bestBic);
        double confidence = Math.Clamp(1.0 - Math.Exp(-0.5 * bicImprovement), 0.0, 1.0);

        return new BaiPerronResult
        {
            Breakpoints = breakpoints,
            BreakCount = bestSegmentCount - 1,
            Confidence = confidence,
            GlobalRSS = bestRss,
            BicScore = bestBic,
            SampleSize = sampleSize,
            IsValid = true,
            Explanation = $"Segments={bestSegmentCount}, ruptures={bestSegmentCount - 1}, RSS={bestRss:F6}, BIC={bestBic:F6}"
        };
    }

    private static IReadOnlyList<int> ReconstructBreakpoints(
        int[,] previousBreak,
        int segmentCount,
        int sampleSize)
    {
        var breakpoints = new List<int>(segmentCount - 1);
        int end = sampleSize;
        for (int currentSegmentCount = segmentCount; currentSegmentCount > 1; currentSegmentCount--)
        {
            int start = previousBreak[currentSegmentCount, end];
            if (start < 0)
                return [];

            breakpoints.Add(start);
            end = start;
        }

        breakpoints.Reverse();
        return breakpoints;
    }
}