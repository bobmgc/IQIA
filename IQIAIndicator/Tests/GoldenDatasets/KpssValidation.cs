using IQIAIndicator.Engine.Regime.Evidence.KPSS;

namespace IQIAIndicator.Tests.GoldenDatasets;

/// <summary>
/// Validation du modèle KPSS IQIA contre Python Statsmodels.
///
/// Métriques :
///   MAE(η̂)    : erreur absolue moyenne sur la statistique
///   RMSE(η̂)   : racine de l'erreur quadratique moyenne
///   ClassMatch : même classification @5% (stationnaire ou non)
///   ΔBandwidth : différence de largeur de bande Newey-West
/// </summary>
public static class KpssValidation
{
    public sealed class Metrics
    {
        public required decimal StatisticAbsError   { get; init; }
        public required decimal PValueAbsError      { get; init; }
        public required bool    ClassificationMatch { get; init; }
        public required int     BandwidthDiff       { get; init; }
        public required string  SeriesName          { get; init; }

        public string Summary() =>
            $"{SeriesName,-22} Δη̂={StatisticAbsError:F4}, " +
            $"Δp={PValueAbsError:F4}, " +
            $"Classification={ClassificationMatch}, " +
            $"ΔBw={BandwidthDiff}";

        // Critère d'acceptabilité : Δη̂ < 0.05, classification identique obligatoire
        public bool IsAcceptable =>
            StatisticAbsError   < 0.05m &&
            ClassificationMatch;
    }

    /// <summary>Exécute le KPSS IQIA sur une série brute et retourne le KpssResult.</summary>
    public static KpssResult RunOnSeries(decimal[] prices, bool withTrend = false)
    {
        int n = prices.Length;
        if (n < 30)
            return KpssResult.Invalid("Série trop courte (minimum 30).");

        var residuals = new decimal[n];
        bool ok = withTrend
            ? KpssRegression.TryDetrend(prices, n, residuals)
            : KpssRegression.TryDemean (prices, n, residuals);

        if (!ok)
            return KpssResult.Invalid("Détrending impossible.");

        decimal lrv = KpssLongRunVariance.Compute(residuals, n);
        if (lrv <= 0m)
            return KpssResult.Invalid("Variance de long terme nulle.");

        decimal stat = KpssStatistics.ComputeStatistic(residuals, n, lrv);
        if (stat < 0m)
            return KpssResult.Invalid("Statistique invalide.");

        var (cv1, cv25, cv5, cv10) = KpssCriticalValues.Get(withTrend);
        decimal pv = KpssStatistics.ApproximatePValue(stat, cv1, cv25, cv5, cv10);

        return new KpssResult
        {
            Statistic       = stat,
            PValue          = pv,
            Confidence      = Math.Clamp((decimal)n / 60, 0m, 1m),
            CriticalValue1  = cv1,
            CriticalValue5  = cv5,
            CriticalValue10 = cv10,
            IsStationary    = stat < cv5,
            Bandwidth       = KpssLongRunVariance.ComputeBandwidth(n),
            SampleSize      = n,
            Explanation     = $"η̂={stat:F4}, p≈{pv:F4}",
            IsValid         = true
        };
    }

    /// <summary>Compare un résultat IQIA à une référence Statsmodels.</summary>
    public static Metrics Compare(
        KpssResult iqia,
        KpssGoldenDataset.StatsmodelsReference reference,
        string seriesName)
    {
        if (!iqia.IsValid)
            return new Metrics
            {
                SeriesName          = seriesName,
                StatisticAbsError   = decimal.MaxValue,
                PValueAbsError      = decimal.MaxValue,
                ClassificationMatch = false,
                BandwidthDiff       = int.MaxValue
            };

        return new Metrics
        {
            SeriesName          = seriesName,
            StatisticAbsError   = Math.Abs(iqia.Statistic - reference.Statistic),
            PValueAbsError      = Math.Abs(iqia.PValue    - reference.PValue),
            ClassificationMatch = iqia.IsStationary == reference.IsStationary,
            BandwidthDiff       = Math.Abs(iqia.Bandwidth  - reference.NLags)
        };
    }

    /// <summary>Rapport de validation sur les 6 séries standard.</summary>
    public static string RunAllReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Validation KPSS IQIA vs Statsmodels ===");
        sb.AppendLine("  kpss(y, regression='c', nlags='legacy')");
        sb.AppendLine();

        RunCase(sb, "WhiteNoise",         KpssGoldenDataset.WhiteNoise(100));
        RunCase(sb, "RandomWalk",         KpssGoldenDataset.RandomWalk(100));
        RunCase(sb, "AR1_phi0.5",         KpssGoldenDataset.Ar1Moderate(100));
        RunCase(sb, "AR1_phi0.95",        KpssGoldenDataset.Ar1NearUnitRoot(100));
        RunCase(sb, "DeterministicTrend", KpssGoldenDataset.DeterministicTrend(100));
        RunCase(sb, "MeanReversion",      KpssGoldenDataset.MeanReversion(100));

        sb.AppendLine();
        sb.AppendLine("Renseigner les colonnes Python avec : kpss(y, regression='c', nlags='legacy')");
        sb.AppendLine("Note : Statsmodels plafonne la p-value à [0.01, 0.10].");
        return sb.ToString();
    }

    private static void RunCase(System.Text.StringBuilder sb, string name, decimal[] prices)
    {
        var r = RunOnSeries(prices);
        if (r.IsValid)
            sb.AppendLine($"{name,-22} η̂={r.Statistic,8:F4}  " +
                          $"p≈{r.PValue,6:F4}  bw={r.Bandwidth}  " +
                          $"stat={r.IsStationary}");
        else
            sb.AppendLine($"{name,-22} ERREUR: {r.Explanation}");
    }
}
