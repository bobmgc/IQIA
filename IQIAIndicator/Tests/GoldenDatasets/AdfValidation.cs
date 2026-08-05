using IQIAIndicator.Engine.Regime.Evidence.ADF;

namespace IQIAIndicator.Tests.GoldenDatasets;

/// <summary>
/// Métriques de comparaison entre l'implémentation IQIA et la référence Statsmodels.
///
/// Utilisation :
///   var metrics = AdfValidation.Compare(iqiaStat, iqiaPValue, reference);
///   Console.WriteLine(metrics.Summary());
/// </summary>
public static class AdfValidation
{
    public sealed class Metrics
    {
        public required decimal StatisticAbsError   { get; init; }  // |τ_IQIA − τ_Python|
        public required decimal PValueAbsError      { get; init; }  // |p_IQIA − p_Python|
        public required bool    ClassificationMatch { get; init; }  // même décision @5%
        public required int     LagDiff             { get; init; }  // |p_IQIA − p_Python|
        public required string  SeriesName          { get; init; }

        public string Summary() =>
            $"{SeriesName}: Δτ={StatisticAbsError:F4}, " +
            $"Δp={PValueAbsError:F4}, " +
            $"Classification={ClassificationMatch}, " +
            $"ΔLag={LagDiff}";

        public bool IsAcceptable =>
            StatisticAbsError   < 0.10m &&   // tolérance τ : ±0.10
            PValueAbsError      < 0.05m &&   // tolérance p  : ±0.05
            ClassificationMatch;             // classification identique obligatoire
    }

    /// <summary>Compare un résultat ADF IQIA avec une référence Statsmodels.</summary>
    public static Metrics Compare(
        AdfResult iqiaResult,
        AdfGoldenDataset.StatsmodelsReference reference,
        string seriesName)
    {
        if (!iqiaResult.IsValid)
            return new Metrics
            {
                SeriesName          = seriesName,
                StatisticAbsError   = decimal.MaxValue,
                PValueAbsError      = decimal.MaxValue,
                ClassificationMatch = false,
                LagDiff             = int.MaxValue
            };

        return new Metrics
        {
            SeriesName          = seriesName,
            StatisticAbsError   = Math.Abs(iqiaResult.Statistic    - reference.Statistic),
            PValueAbsError      = Math.Abs(iqiaResult.PValue        - reference.PValue),
            ClassificationMatch = iqiaResult.IsStationary == reference.IsStationary,
            LagDiff             = Math.Abs(iqiaResult.LagUsed       - reference.LagUsed)
        };
    }

    /// <summary>
    /// Exécute le modèle ADF IQIA sur une série et retourne l'AdfResult interne.
    /// Permet la comparaison directe avec Statsmodels sans passer par le MarketContext.
    /// </summary>
    public static AdfResult RunOnSeries(decimal[] prices)
    {
        if (prices.Length < 30)
            return AdfResult.Invalid("Série trop courte (minimum 30).");

        int n   = prices.Length;
        int lag = AdfStatistics.SelectLag(prices, n);

        if (!AdfRegression.TryCompute(prices, n, lag,
                out decimal tStat, out _, out int nObs))
            return AdfResult.Invalid("Régression MCO échouée.");

        var (cv1, cv5, cv10) = AdfCriticalValues.Get(nObs);
        decimal pv           = AdfStatistics.ApproximatePValue(tStat, cv1, cv5, cv10);
        bool stationary      = tStat < cv5;

        return new AdfResult
        {
            Statistic       = tStat,
            PValue          = pv,
            Confidence      = Math.Clamp((decimal)n / 60, 0m, 1m),
            CriticalValue1  = cv1,
            CriticalValue5  = cv5,
            CriticalValue10 = cv10,
            IsStationary    = stationary,
            LagUsed         = lag,
            SampleSize      = nObs,
            Explanation     = $"τ={tStat:F4}, p≈{pv:F4}, ADF({lag})",
            IsValid         = true
        };
    }

    /// <summary>Rapport de validation sur les 6 séries standard.</summary>
    public static string RunAllReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Validation ADF IQIA vs Statsmodels ===");
        sb.AppendLine();

        RunCase(sb, "WhiteNoise",       AdfGoldenDataset.WhiteNoise(100));
        RunCase(sb, "RandomWalk",       AdfGoldenDataset.RandomWalk(100));
        RunCase(sb, "AR1_phi0.5",       AdfGoldenDataset.Ar1Moderate(100));
        RunCase(sb, "AR1_phi0.95",      AdfGoldenDataset.Ar1NearUnitRoot(100));
        RunCase(sb, "DeterministicTrend", AdfGoldenDataset.DeterministicTrend(100));
        RunCase(sb, "MeanReversion",    AdfGoldenDataset.MeanReversion(100));

        sb.AppendLine();
        sb.AppendLine("Renseigner les colonnes Python avec : adfuller(y, autolag='AIC', regression='c')");
        return sb.ToString();
    }

    private static void RunCase(System.Text.StringBuilder sb, string name, decimal[] prices)
    {
        var result = RunOnSeries(prices);
        if (result.IsValid)
            sb.AppendLine($"{name,-22} τ={result.Statistic,8:F4}  " +
                          $"p≈{result.PValue,6:F4}  lag={result.LagUsed}  " +
                          $"stat={result.IsStationary}");
        else
            sb.AppendLine($"{name,-22} ERREUR: {result.Explanation}");
    }
}
