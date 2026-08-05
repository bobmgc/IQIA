namespace IQIAIndicator.Engine.Regime.Evidence.ADF;

/// <summary>
/// Valeurs critiques du test ADF issues de la surface de réponse de MacKinnon (1994).
///
/// Formule : cv(T) = β_∞ + β₁/T + β₂/T²
///
/// Source : MacKinnon, J.G. (1994). "Approximate Asymptotic Distribution Functions
///   for Unit-Root and Cointegration Tests." Journal of Business &amp; Economic Statistics,
///   12(2), pp. 167–176. Table 1.
///
/// Spécification "constante uniquement" (regression='c' dans Statsmodels).
/// Pour "constante + tendance", utiliser WithTrend=true.
/// </summary>
internal static class AdfCriticalValues
{
    // ── Constante uniquement (T2 / nreg=1 dans MacKinnon 1994) ─────────────
    // Row order: [1%, 5%, 10%]
    private static readonly (decimal BInf, decimal B1, decimal B2)[] ConstantOnly =
    [
        (-3.43035m, -6.5393m, -16.786m),  // 1%
        (-2.86154m, -2.8645m,  -4.234m),  // 5%
        (-2.56677m, -1.5384m,  -2.809m),  // 10%
    ];

    // ── Constante + tendance déterministe (T3 / nreg=2 dans MacKinnon 1994) ─
    private static readonly (decimal BInf, decimal B1, decimal B2)[] ConstantAndTrend =
    [
        (-3.95877m, -9.0531m, -28.428m),  // 1%
        (-3.41090m, -4.3904m,  -9.036m),  // 5%
        (-3.12705m, -2.5815m,  -4.455m),  // 10%
    ];

    // ── Calcul ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Retourne les valeurs critiques (cv1%, cv5%, cv10%) pour une taille T.
    /// </summary>
    internal static (decimal Cv1, decimal Cv5, decimal Cv10) Get(int T, bool withTrend = false)
    {
        var table = withTrend ? ConstantAndTrend : ConstantOnly;
        var t = (decimal)T;
        return (
            Compute(table[0], t),
            Compute(table[1], t),
            Compute(table[2], t)
        );
    }

    private static decimal Compute(in (decimal BInf, decimal B1, decimal B2) c, decimal T) =>
        c.BInf + c.B1 / T + c.B2 / (T * T);
}
