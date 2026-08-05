namespace IQIAIndicator.Engine.Regime.Evidence.DFA;

/// <summary>
/// Régression OLS linéaire utilisée par le DFA.
/// Deux fonctions :
///   FitLinear       — cas général y = a + b·x
///   ComputeSegmentF2 — détrending optimisé pour t = 0,1,…,n−1 (fenêtre DFA)
///
/// Aucune connaissance du DFA ou du domaine financier.
/// </summary>
internal static class DfaRegression
{
    // ── Régression générale y = a + b·x ──────────────────────────────────────
    // Retourne false si la matrice est singulière.

    internal static bool FitLinear(
        double[] x, double[] y, int n,
        out double slope, out double intercept, out double r2)
    {
        slope = 0.0; intercept = 0.0; r2 = 0.0;
        if (n < 2) return false;

        double sx = 0.0, sy = 0.0, sxy = 0.0, sx2 = 0.0;
        for (int i = 0; i < n; i++)
        {
            sx  += x[i]; sy  += y[i];
            sxy += x[i] * y[i];
            sx2 += x[i] * x[i];
        }

        double den = n * sx2 - sx * sx;
        if (Math.Abs(den) < 1e-15) return false;

        slope     = (n * sxy - sx * sy) / den;
        intercept = (sy - slope * sx) / n;

        double meanY = sy / n, ssTot = 0.0, ssRes = 0.0;
        for (int i = 0; i < n; i++)
        {
            double d   = y[i] - meanY;     ssTot += d * d;
            double res = y[i] - (intercept + slope * x[i]); ssRes += res * res;
        }
        r2 = ssTot > 1e-15 ? 1.0 - ssRes / ssTot : 0.0;
        return true;
    }

    // ── Détrending optimisé pour une fenêtre DFA ─────────────────────────────
    // t = 0,1,…,n−1  → utilise les identités fermées pour Σt et Σt².
    //
    // Retourne F²_v = (1/n) · Σ (y[start+t] − tendance)²
    //
    // Identités utilisées :
    //   Σt = n(n−1)/2
    //   t̄  = (n−1)/2
    //   Σ(t−t̄)² = n(n²−1)/12   (variance des entiers 0..n−1 multipliée par n)

    internal static double ComputeSegmentF2(double[] profile, int start, int n)
    {
        double tBar   = (n - 1.0) / 2.0;
        double sTdev2 = n * ((double)n * n - 1.0) / 12.0;
        if (sTdev2 < 1e-15) return 0.0;

        double sy = 0.0, stdy = 0.0;
        for (int t = 0; t < n; t++)
        {
            double v = profile[start + t];
            sy   += v;
            stdy += (t - tBar) * v;
        }

        double slope     = stdy / sTdev2;
        double intercept = sy / n - slope * tBar;

        double f2 = 0.0;
        for (int t = 0; t < n; t++)
        {
            double res = profile[start + t] - (intercept + slope * t);
            f2 += res * res;
        }
        return f2 / n;
    }
}
