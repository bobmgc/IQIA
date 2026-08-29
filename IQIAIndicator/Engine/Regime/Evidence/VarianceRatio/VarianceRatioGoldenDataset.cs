namespace IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;

/// <summary>
/// Series de prix deterministes pour la validation du test de Variance Ratio.
/// </summary>
public static class VarianceRatioGoldenDataset
{
    public static double[] WhiteNoise(int sampleSize = 256)
    {
        double[] innovations = GenerateGaussian(42UL, sampleSize - 1);
        return PricesFromLogReturns(innovations, 0.01);
    }

    public static double[] RandomWalk(int sampleSize = 256)
    {
        double[] innovations = GenerateGaussian(42UL, sampleSize);
        var prices = new double[sampleSize];
        prices[0] = 100.0;
        for (int i = 1; i < sampleSize; i++)
            prices[i] = prices[i - 1] + 0.1 * innovations[i];
        return prices;
    }

    public static double[] PositiveAr1(int sampleSize = 256)
    {
        double[] innovations = GenerateGaussian(42UL, sampleSize - 1);
        var returns = new double[innovations.Length];
        for (int i = 0; i < returns.Length; i++)
            returns[i] = (i == 0 ? 0.0 : 0.6 * returns[i - 1]) + 0.01 * innovations[i];
        return PricesFromLogReturns(returns, 1.0);
    }

    public static double[] NegativeAr1(int sampleSize = 256)
    {
        double[] innovations = GenerateGaussian(42UL, sampleSize - 1);
        var returns = new double[innovations.Length];
        for (int i = 0; i < returns.Length; i++)
            returns[i] = (i == 0 ? 0.0 : -0.6 * returns[i - 1]) + 0.01 * innovations[i];
        return PricesFromLogReturns(returns, 1.0);
    }

    public static double[] OuProcess(int sampleSize = 256)
    {
        double[] innovations = GenerateGaussian(42UL, sampleSize);
        var levels = new double[sampleSize];
        for (int i = 1; i < levels.Length; i++)
            levels[i] = 0.8 * levels[i - 1] + 0.02 * innovations[i];

        var prices = new double[sampleSize];
        for (int i = 0; i < prices.Length; i++)
            prices[i] = 100.0 * Math.Exp(levels[i]);
        return prices;
    }

    public static double[] Trend(int sampleSize = 256)
    {
        double[] innovations = GenerateGaussian(42UL, sampleSize - 1);
        var returns = new double[innovations.Length];
        for (int i = 0; i < returns.Length; i++)
            returns[i] = 0.002 + 0.002 * innovations[i];
        return PricesFromLogReturns(returns, 1.0);
    }

    private static double[] PricesFromLogReturns(IReadOnlyList<double> returns, double scale)
    {
        var prices = new double[returns.Count + 1];
        prices[0] = 100.0;
        for (int i = 0; i < returns.Count; i++)
            prices[i + 1] = prices[i] * Math.Exp(scale * returns[i]);
        return prices;
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