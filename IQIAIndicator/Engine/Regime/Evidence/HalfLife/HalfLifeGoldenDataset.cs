namespace IQIAIndicator.Engine.Regime.Evidence.HalfLife;

/// <summary>
/// Series deterministes pour valider l'estimateur Half-Life.
/// </summary>
public static class HalfLifeGoldenDataset
{
    public static double[] WhiteNoise(int sampleSize = 256) => GenerateGaussian(42UL, sampleSize);

    public static double[] RandomWalk(int sampleSize = 256)
    {
        double[] innovations = GenerateGaussian(42UL, sampleSize);
        var series = new double[sampleSize];
        for (int i = 1; i < sampleSize; i++)
            series[i] = series[i - 1] + innovations[i];
        return series;
    }

    public static double[] OuProcess(int sampleSize = 256)
    {
        const double kappa = 0.5;
        double[] innovations = GenerateGaussian(42UL, sampleSize);
        var series = new double[sampleSize];
        for (int i = 1; i < sampleSize; i++)
            series[i] = series[i - 1] + kappa * (0.0 - series[i - 1]) + innovations[i];
        return series;
    }

    public static double[] Ar1(int sampleSize = 256)
    {
        const double phi = 0.8;
        double[] innovations = GenerateGaussian(42UL, sampleSize);
        var series = new double[sampleSize];
        for (int i = 1; i < sampleSize; i++)
            series[i] = phi * series[i - 1] + innovations[i];
        return series;
    }

    public static double[] Trend(int sampleSize = 256)
    {
        double[] innovations = GenerateGaussian(42UL, sampleSize);
        var series = new double[sampleSize];
        for (int i = 0; i < sampleSize; i++)
            series[i] = 100.0 * Math.Exp(0.01 * i) + 0.001 * innovations[i];
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