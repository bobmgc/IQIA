namespace IQIAIndicator.Engine.Regime.Evidence.CUSUM;

/// <summary>
/// Series deterministes pour la validation du detecteur CUSUM.
/// </summary>
public static class CusumGoldenDataset
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

    public static double[] MeanShift(int sampleSize = 256)
    {
        double[] series = GenerateGaussian(42UL, sampleSize);
        int breakIndex = sampleSize / 2;
        for (int i = breakIndex; i < series.Length; i++)
            series[i] += 1.0;
        return series;
    }

    public static double[] VarianceShift(int sampleSize = 256)
    {
        double[] series = GenerateGaussian(42UL, sampleSize);
        int breakIndex = sampleSize / 2;
        for (int i = breakIndex; i < series.Length; i++)
            series[i] *= 2.0;
        return series;
    }

    public static double[] OuProcess(int sampleSize = 256)
    {
        double[] innovations = GenerateGaussian(42UL, sampleSize);
        var series = new double[sampleSize];
        for (int i = 1; i < series.Length; i++)
            series[i] = 0.8 * series[i - 1] + innovations[i];
        return series;
    }

    public static double[] TrendBreak(int sampleSize = 256)
    {
        double[] series = GenerateGaussian(42UL, sampleSize);
        int breakIndex = sampleSize / 2;
        for (int i = breakIndex; i < series.Length; i++)
            series[i] += 0.1 * (i - breakIndex);
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