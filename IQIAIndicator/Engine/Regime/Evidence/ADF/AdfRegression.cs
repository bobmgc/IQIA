namespace IQIAIndicator.Engine.Regime.Evidence.ADF;

/// <summary>
/// Régression ADF par MCO (Moindres Carrés Ordinaires).
///
/// Modèle : Δy_t = α + β·y_{t-1} + γ₁·Δy_{t-1} + … + γₚ·Δy_{t-p} + ε_t
///
/// Paramètre clé : β (coefficient sur le niveau retardé y_{t-1}).
/// Statistique de test : τ = β̂ / SE(β̂)
///
/// La p-value est celle de la distribution de Dickey-Fuller (non standard).
/// </summary>
internal static class AdfRegression
{
    /// <summary>
    /// Sprint 15.1 (SCI15-01) robustness bound. Not a statistical threshold - the ADF formula, AIC
    /// selection, and MacKinnon critical values are untouched by this constant. Purely a numeric
    /// safety margin: the accumulation loop below sums up to ~n products of two series-derived
    /// decimal values (y_{t-1}, or a Δy difference of two such values), so squaring a single value
    /// already this large - let alone summing dozens of such squares - risks exceeding decimal's
    /// ~7.9x10^28 range. 1e12 leaves roughly 4 orders of magnitude of headroom after squaring
    /// (1e12^2 = 1e24) and summing up to ~100 terms (1e26), while remaining many orders of magnitude
    /// above any realistic price series - including the existing AssertAdfExtremeValuesDoNotCrash
    /// scale-stress test (~1e8), which must and does continue to compute normally, not report
    /// Invalid. This guard exists purely to give a specific, honest diagnostic for a value that is
    /// clearly out-of-range for this computation; it is not the only protection (see the try/catch
    /// below, which is the actual safety net for any accumulation this single-value check doesn't
    /// anticipate).
    /// </summary>
    internal const decimal SafeMagnitudeBound = 1_000_000_000_000m;

    internal static bool HasOutOfRangeMagnitude(decimal[] y, int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (Math.Abs(y[i]) > SafeMagnitudeBound)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Calcule la statistique ADF τ et le critère AIC pour un nombre de lags p donné.
    /// y : série de prix, y[0] = plus ancien, y[n-1] = plus récent.
    /// Retourne false si les données sont insuffisantes, la magnitude hors limites représentables,
    /// la matrice singulière, ou si l'accumulation numérique déborde malgré la garde de magnitude
    /// (Sprint 15.1 - defense in depth, formule et seuils statistiques inchangés).
    /// </summary>
    internal static bool TryCompute(
        decimal[] y, int n, int p,
        out decimal tStat, out decimal aic, out int nObs)
    {
        tStat = decimal.MaxValue;
        aic   = decimal.MaxValue;
        nObs  = 0;

        int k    = p + 2;          // paramètres : intercept, y_{t-1}, p·Δy retardés
        nObs     = n - 1 - p;      // observations effectives dans la régression
        int minObs = k + 3;        // au moins k+3 degrés de liberté

        if (nObs < minObs) return false;
        if (HasOutOfRangeMagnitude(y, n)) return false;

        try
        {
            // ── Accumulateurs X'X (k×k) et X'y (k) ──────────────────────
            var XtX = new decimal[k * k];
            var Xty = new decimal[k];
            var xBuf = new decimal[k];

            for (int i = p + 1; i < n; i++)
            {
                decimal yi = y[i] - y[i - 1];  // Δy_t : variable dépendante

                // Vecteur régresseur : [1, y_{t-1}, Δy_{t-1}, ..., Δy_{t-p}]
                xBuf[0] = 1m;
                xBuf[1] = y[i - 1];
                for (int j = 2; j < k; j++)
                    xBuf[j] = y[i - j + 1] - y[i - j];  // Δy_{t-(j-1)}

                for (int r = 0; r < k; r++)
                {
                    Xty[r] += xBuf[r] * yi;
                    for (int c = 0; c < k; c++)
                        XtX[r * k + c] += xBuf[r] * xBuf[c];
                }
            }

            // ── Résoudre (X'X)·β̂ = X'y ───────────────────────────────
            var A_beta = AdfMath.Clone(XtX, k * k);
            var b_beta = AdfMath.Clone(Xty, k);
            if (!AdfMath.SolveLinearSystem(A_beta, b_beta, k)) return false;
            // b_beta contient maintenant β̂

            // ── Résidus et σ² ─────────────────────────────────────────
            decimal ssr = 0m;
            for (int i = p + 1; i < n; i++)
            {
                decimal yi = y[i] - y[i - 1];
                xBuf[0] = 1m;
                xBuf[1] = y[i - 1];
                for (int j = 2; j < k; j++)
                    xBuf[j] = y[i - j + 1] - y[i - j];

                decimal yhat = 0m;
                for (int r = 0; r < k; r++) yhat += b_beta[r] * xBuf[r];
                decimal e = yi - yhat;
                ssr += e * e;
            }

            int dof = nObs - k;
            if (dof <= 0 || ssr <= 0m) return false;
            decimal sigma2 = ssr / dof;

            // ── Colonne 1 de (X'X)⁻¹ pour SE(β̂_level) ───────────────
            // Résoudre (X'X)·v = e₁ → v[1] = (X'X)⁻¹[1,1]
            var A_inv  = AdfMath.Clone(XtX, k * k);
            var e1     = new decimal[k];
            e1[1]      = 1m;
            if (!AdfMath.SolveLinearSystem(A_inv, e1, k)) return false;
            decimal invDiag1 = e1[1];

            if (invDiag1 <= 0m) return false;

            decimal se = (decimal)Math.Sqrt((double)(sigma2 * invDiag1));
            if (se == 0m) return false;

            tStat = b_beta[1] / se;

            // AIC = nObs·ln(SSR/nObs) + 2k  (critère d'information d'Akaike)
            aic = (decimal)(nObs * Math.Log((double)(ssr / nObs))) + 2m * k;

            return true;
        }
        catch (OverflowException)
        {
            // Defense in depth (Sprint 15.1, SCI15-01): the magnitude guard above rejects any single
            // out-of-range series value before this point; this catch exists for the case the guard
            // does not anticipate - many individually in-range values whose products/sums still
            // exceed decimal's range once accumulated across the regression. No statistic is computed
            // from a partially-overflowed state; the caller sees exactly the same "regression failed"
            // outcome as any other numerical failure in this method (singular matrix, non-finite SE,
            // etc.), never a crash.
            tStat = decimal.MaxValue;
            aic = decimal.MaxValue;
            return false;
        }
    }
}
