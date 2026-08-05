namespace IQIAIndicator.Engine.Regime.Evidence.BaiPerron;

/// <summary>
/// Series deterministes pour la validation de la segmentation Bai-Perron.
/// </summary>
public static class BaiPerronGoldenDataset
{
    public static double[] WhiteNoise(int sampleSize = 256) => GenerateGaussian(42UL, sampleSize);

    public static double[] RandomWalk(int sampleSize = 256)
    {
        double[] innovations = GenerateGaussian(42UL, sampleSize);
        var series = new double[sampleSize];
        for (int i = 1; i < series.Length; i++)
            series[i] = series[i - 1] + innovations[i];
        return series;
    }

    public static double[] SingleMeanShift(int sampleSize = 256)
    {
        double[] series = GenerateGaussian(42UL, sampleSize);
        int breakpoint = sampleSize / 2;
        for (int i = breakpoint; i < series.Length; i++)
            series[i] += 3.0;
        return series;
    }

    public static double[] DoubleMeanShift(int sampleSize = 256)
    {
        double[] series = GenerateGaussian(42UL, sampleSize);
        int firstBreakpoint = sampleSize / 3;
        int secondBreakpoint = 2 * sampleSize / 3;
        for (int i = firstBreakpoint; i < secondBreakpoint; i++)
            series[i] += 3.0;
        for (int i = secondBreakpoint; i < series.Length; i++)
            series[i] -= 2.0;
        return series;
    }

    public static double[] TrendBreak(int sampleSize = 256)
    {
        double[] innovations = GenerateGaussian(42UL, sampleSize);
        var series = new double[sampleSize];
        int breakpoint = sampleSize / 2;
        for (int i = 0; i < series.Length; i++)
        {
            double trend = i < breakpoint
                ? 0.02 * i
                : 0.02 * breakpoint - 0.04 * (i - breakpoint);
            series[i] = trend + innovations[i];
        }

        return series;
    }

    public static double[] TripleStructuralBreak(int sampleSize = 256)
    {
        double[] series = GenerateGaussian(42UL, sampleSize);
        int firstBreakpoint = sampleSize / 4;
        int secondBreakpoint = sampleSize / 2;
        int thirdBreakpoint = 3 * sampleSize / 4;
        for (int i = firstBreakpoint; i < secondBreakpoint; i++)
            series[i] += 2.5;
        for (int i = secondBreakpoint; i < thirdBreakpoint; i++)
            series[i] -= 2.0;
        for (int i = thirdBreakpoint; i < series.Length; i++)
            series[i] += 3.5;
        return series;
    }

    private static double[] GenerateGaussian(ulong seed, int sampleSize)
    {
        var values = new double[sampleSize];
        for (int i = 0; i < sampleSize; i += 2)
        {
            seed = seed * 6364136223846793005UL + 1442695040888963407UL;
            double firstUniform = (seed >> 11) / (double)(1UL << 53) + 1e-15;
            seed = seed * 6364136223846793005UL + 1442695040888963407UL;
            double secondUniform = (seed >> 11) / (double)(1UL << 53);
            double radius = Math.Sqrt(-2.0 * Math.Log(firstUniform));
            values[i] = radius * Math.Cos(2.0 * Math.PI * secondUniform);
            if (i + 1 < sampleSize)
                values[i + 1] = radius * Math.Sin(2.0 * Math.PI * secondUniform);
        }

        return values;
    }
}