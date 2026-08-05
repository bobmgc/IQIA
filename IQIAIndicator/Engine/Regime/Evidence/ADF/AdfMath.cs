namespace IQIAIndicator.Engine.Regime.Evidence.ADF;

/// <summary>
/// Résolution de systèmes linéaires par élimination de Gauss avec pivot partiel.
/// Utilisé par AdfRegression pour résoudre (X'X)β = X'y.
/// </summary>
internal static class AdfMath
{
    // Tolérance pour la détection de singularité (en décimal)
    private const decimal PivotTolerance = 1e-15m;

    /// <summary>
    /// Résout A·x = b par élimination de Gauss avec pivot partiel.
    /// A est modifié en place. b est remplacé par la solution x.
    /// A est stocké en ligne-majeure : A[i*k + j] = A_{i,j}.
    /// Retourne false si la matrice est singulière ou mal conditionnée.
    /// </summary>
    internal static bool SolveLinearSystem(decimal[] A, decimal[] b, int k)
    {
        for (int col = 0; col < k; col++)
        {
            // ── Pivot partiel ─────────────────────────────────────────
            int pivotRow = col;
            decimal maxAbs = Math.Abs(A[col * k + col]);
            for (int row = col + 1; row < k; row++)
            {
                decimal abs = Math.Abs(A[row * k + col]);
                if (abs > maxAbs) { maxAbs = abs; pivotRow = row; }
            }
            if (maxAbs < PivotTolerance) return false;

            // ── Échange de lignes ─────────────────────────────────────
            if (pivotRow != col)
            {
                for (int j = 0; j < k; j++)
                    (A[col * k + j], A[pivotRow * k + j]) = (A[pivotRow * k + j], A[col * k + j]);
                (b[col], b[pivotRow]) = (b[pivotRow], b[col]);
            }

            // ── Élimination vers le bas ───────────────────────────────
            decimal pivot = A[col * k + col];
            for (int row = col + 1; row < k; row++)
            {
                decimal factor = A[row * k + col] / pivot;
                if (factor == 0m) continue;
                A[row * k + col] = 0m;
                for (int j = col + 1; j < k; j++)
                    A[row * k + j] -= factor * A[col * k + j];
                b[row] -= factor * b[col];
            }
        }

        // ── Remontée ─────────────────────────────────────────────────
        for (int col = k - 1; col >= 0; col--)
        {
            for (int j = col + 1; j < k; j++)
                b[col] -= A[col * k + j] * b[j];
            b[col] /= A[col * k + col];
        }
        return true;
    }

    /// <summary>Copie sûre d'un tableau decimal[].</summary>
    internal static decimal[] Clone(decimal[] src, int len)
    {
        var dst = new decimal[len];
        Array.Copy(src, dst, len);
        return dst;
    }
}
