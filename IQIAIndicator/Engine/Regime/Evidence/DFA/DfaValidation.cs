using System.Linq;

namespace IQIAIndicator.Engine.Regime.Evidence.DFA;

/// <summary>
/// Framework de validation du DFA IQIA contre Python nolds.
///
/// Métriques calculées :
///   AbsError : |H_IQIA − H_Python|
///   RelError : AbsError / |H_Python|
///   R² gap   : différence de R²
///   MAE/RMSE : sur une batterie de séries
///
/// Critère d'acceptabilité : AbsError &lt; 0.05 pour H ∈ [0.3, 0.8]
/// </summary>
public static class DfaValidation
{
    public sealed class Metrics
    {
        public required string SeriesName  { get; init; }
        public required double AbsError    { get; init; }   // |H_IQIA − H_ref|
        public required double RelError    { get; init; }   // AbsError / H_ref
        public required double RSquared    { get; init; }   // R² du DFA IQIA
        public required int    WindowCount { get; init; }
        public required bool   IsValid     { get; init; }

        public string Summary() =>
            $"{SeriesName,-26} H_err={AbsError:F4}, R²={RSquared:F3}, " +
            $"win={WindowCount}, ok={IsValid}";

        // AbsError < 0.05 pour H ∈ (0.3, 0.8) — tolérance scientifique raisonnable
        public bool IsAcceptable => AbsError < 0.05 && IsValid;
    }

    /// <summary>Exécute le DFA IQIA directement sur une série double[].</summary>
    public static DfaResult RunOnSeries(double[] series)
    {
        int n = series.Length;
        if (n < 30) return DfaResult.Invalid("Série trop courte (minimum 30).");

        var returns = new double[n - 1];
        var profile = new double[n - 1];

        // Pour les séries de prix : log-rendements
        // Pour les séries déjà stationnaires (rendements, bruit) : utiliser directement
        if (series[0] > 1.0 && Array.TrueForAll(series, v => v > 0.0))
        {
            // Série de prix → log-rendements
            if (!DfaMath.TryLogReturns(series, n, returns))
                return DfaResult.Invalid("Prix invalides.");
        }
        else
        {
            // Série stationnaire (rendements, bruit, etc.) → utiliser directement
            for (int i = 0; i < n - 1; i++) returns[i] = series[i];
            n--;
        }

        int nR     = n - 1;
        double mu  = DfaMath.Mean(returns, nR);
        DfaMath.IntegrateProfile(returns, nR, mu, profile);

        int[]  sizes = DfaStatistics.GenerateWindowSizes(nR);
        if (sizes.Length < 4) return DfaResult.Invalid("Trop peu de fenêtres.");

        var validSizes  = new List<int>(sizes.Length);
        var validFlucts = new List<double>(sizes.Length);

        foreach (int s in sizes)
        {
            double f = DfaStatistics.ComputeFluctuation(profile, nR, s);
            if (!double.IsNaN(f) && f > 0.0) { validSizes.Add(s); validFlucts.Add(f); }
        }

        int valid = validSizes.Count;
        if (valid < 4) return DfaResult.Invalid("Trop peu de fenêtres valides.");

        if (!DfaStatistics.EstimateHurst([.. validSizes], [.. validFlucts], valid,
                out double h, out double r2))
            return DfaResult.Invalid("Régression log-log échouée.");

        return new DfaResult
        {
            Hurst        = Math.Clamp(h, 0.0, 2.0),
            RSquared     = r2,
            Confidence   = r2 * Math.Min(1.0, valid / 6.0),
            WindowCount  = valid,
            IsValid      = true,
            WindowSizes  = [.. validSizes.ConvertAll(x => (double)x)],
            Fluctuations = [.. validFlucts],
            Explanation  = $"H={h:F4}, R²={r2:F3}, {valid} fenêtres"
        };
    }

    /// <summary>Compare un résultat IQIA avec une référence Python.</summary>
    public static Metrics Compare(
        DfaResult result,
        DfaGoldenDataset.NoldsReference reference)
    {
        if (!result.IsValid)
            return new Metrics
            {
                SeriesName  = reference.SeriesName, AbsError = double.MaxValue,
                RelError = double.MaxValue, RSquared = 0.0, WindowCount = 0, IsValid = false
            };

        double abs = Math.Abs(result.Hurst - reference.H);
        double rel = reference.H > 1e-6 ? abs / reference.H : abs;

        return new Metrics
        {
            SeriesName  = reference.SeriesName,
            AbsError    = abs,
            RelError    = rel,
            RSquared    = result.RSquared,
            WindowCount = result.WindowCount,
            IsValid     = result.IsValid
        };
    }

    /// <summary>Rapport MAE/RMSE sur les 6 séries standard.</summary>
    public static string RunAllReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Validation DFA IQIA ===");
        sb.AppendLine("  nolds.dfa(series, fit_exp='poly') — OLS pour comparaison exacte");
        sb.AppendLine();

        RunCase(sb, "WhiteNoise",     DfaGoldenDataset.WhiteNoise());
        RunCase(sb, "RandomWalk",     DfaGoldenDataset.RandomWalkPrices());
        RunCase(sb, "Persistent_φ.9", DfaGoldenDataset.PersistentAr1());
        RunCase(sb, "OU_κ=0.5",       DfaGoldenDataset.OuProcess());
        RunCase(sb, "fBm_proxy",       DfaGoldenDataset.FractionalBrownianProxy());
        RunCase(sb, "AntiPers_φ-.8",  DfaGoldenDataset.AntiPersistentAr1());

        sb.AppendLine();
        sb.AppendLine("Renseigner les colonnes Python avec : nolds.dfa(series, fit_exp='poly')");
        return sb.ToString();
    }

    private static void RunCase(System.Text.StringBuilder sb, string name, double[] series)
    {
        var r = RunOnSeries(series);
        if (r.IsValid)
            sb.AppendLine($"{name,-26} H={r.Hurst,6:F4}  R²={r.RSquared:F3}  win={r.WindowCount}");
        else
            sb.AppendLine($"{name,-26} ERREUR: {r.Explanation}");
    }
}
