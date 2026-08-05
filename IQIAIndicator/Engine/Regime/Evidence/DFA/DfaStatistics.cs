namespace IQIAIndicator.Engine.Regime.Evidence.DFA;

/// <summary>
/// Algorithme DFA-1 complet (Detrended Fluctuation Analysis, ordre 1).
///
/// Pipeline :
///   1. GenerateWindowSizes — tailles logarithmiquement espacées en [8, N/4]
///   2. ComputeFluctuation  — F(n) pour une taille de fenêtre donnée
///   3. EstimateHurst       — régression log(F) ~ log(n) → pente = H
///
/// Référence : Peng C-K et al. (1994), Phys. Rev. E 49, 1685.
/// </summary>
internal static class DfaStatistics
{
    private const int MinWindowSize      = 8;
    private const int MinSegmentsPerWin  = 4;  // N/n ≥ 4 → minimum de statistiques
    private const int TargetWindowCount  = 10;
    private const int MinValidWindows    = 4;   // régression log-log fiable

    // ── Génération des tailles de fenêtres ────────────────────────────────────
    //
    // Génère TargetWindowCount tailles dans [minW, N/4], logarithmiquement espacées.
    // Filtre les tailles qui ne produisent pas assez de segments.
    // Retourne [] si la série est trop courte.

    internal static int[] GenerateWindowSizes(int N)
    {
        int maxW = N / MinSegmentsPerWin;
        if (maxW < MinWindowSize) return [];

        var result = new SortedSet<int>();
        for (int i = 0; i < TargetWindowCount; i++)
        {
            double t   = (double)i / (TargetWindowCount - 1);
            double lnW = Math.Log(MinWindowSize) + t * Math.Log((double)maxW / MinWindowSize);
            int    w   = Math.Clamp((int)Math.Round(Math.Exp(lnW)), MinWindowSize, maxW);
            if (N / w >= MinSegmentsPerWin) result.Add(w);
        }
        return [.. result];
    }

    // ── Fluctuation F(n) pour une taille de fenêtre ───────────────────────────
    //
    // Passes avant et arrière (2·Ns segments au total) :
    //   Avant   : [0..n−1], [n..2n−1], …, [(Ns−1)n..(Ns·n−1)]
    //   Arrière : [N−n..N−1], [N−2n..N−n−1], …, [N−Ns·n..N−(Ns−1)n−1]
    //
    // F(n) = sqrt( (1/2Ns) · Σ F²_v )   (Éq. 1, Peng 1994)
    //
    // Retourne NaN si Ns < 2.

    internal static double ComputeFluctuation(double[] profile, int N, int n)
    {
        int Ns = N / n;
        if (Ns < 2) return double.NaN;

        double sumF2 = 0.0;

        for (int v = 0; v < Ns; v++)
            sumF2 += DfaRegression.ComputeSegmentF2(profile, v * n, n);

        for (int v = 0; v < Ns; v++)
            sumF2 += DfaRegression.ComputeSegmentF2(profile, N - (v + 1) * n, n);

        return Math.Sqrt(sumF2 / (2.0 * Ns));
    }

    // ── Estimation de H par régression log-log ────────────────────────────────
    //
    // log F(n) = H · log n + const  →  pente = H
    //
    // Retourne false si la régression est dégénérée ou si trop peu de points.

    internal static bool EstimateHurst(
        int[]    windowSizes,
        double[] fluctuations,
        int      count,
        out double hurst,
        out double r2)
    {
        hurst = 0.5; r2 = 0.0;
        if (count < MinValidWindows) return false;

        var logW = new double[count];
        var logF = new double[count];
        for (int i = 0; i < count; i++)
        {
            logW[i] = Math.Log(windowSizes[i]);
            logF[i] = Math.Log(fluctuations[i]);
        }

        return DfaRegression.FitLinear(logW, logF, count, out hurst, out _, out r2);
    }
}
