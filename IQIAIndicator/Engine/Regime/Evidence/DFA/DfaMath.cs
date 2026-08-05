namespace IQIAIndicator.Engine.Regime.Evidence.DFA;

/// <summary>
/// Utilitaires mathématiques purs pour le DFA.
/// Aucune connaissance du domaine financier ou du régime de marché.
/// </summary>
internal static class DfaMath
{
    // ── Moyenne ──────────────────────────────────────────────────────────────

    internal static double Mean(double[] arr, int n)
    {
        double sum = 0.0;
        for (int i = 0; i < n; i++) sum += arr[i];
        return sum / n;
    }

    // ── Profil intégré ────────────────────────────────────────────────────────
    // Y[k] = Σ_{i=0}^{k} (arr[i] − mean)
    // profile doit être de longueur ≥ n

    internal static void IntegrateProfile(double[] arr, int n, double mean, double[] profile)
    {
        double cum = 0.0;
        for (int i = 0; i < n; i++)
        {
            cum       += arr[i] - mean;
            profile[i] = cum;
        }
    }

    // ── Log-rendements ────────────────────────────────────────────────────────
    // r[i] = ln(prices[i+1] / prices[i])  pour i = 0 .. n−2
    // returns doit être de longueur ≥ n−1

    internal static bool TryLogReturns(double[] prices, int n, double[] returns)
    {
        for (int i = 0; i < n - 1; i++)
        {
            if (prices[i] <= 0.0 || prices[i + 1] <= 0.0) return false;
            returns[i] = Math.Log(prices[i + 1] / prices[i]);
        }
        return true;
    }

    // ── Variance ──────────────────────────────────────────────────────────────

    internal static double Variance(double[] arr, int n, double mean)
    {
        double sum = 0.0;
        for (int i = 0; i < n; i++) { double d = arr[i] - mean; sum += d * d; }
        return sum / n;
    }
}
