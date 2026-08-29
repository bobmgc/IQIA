namespace IQIAIndicator.Tests.Research.StructuralBreakInformationAudit.Support;

/// <summary>One maximal run of consecutive, exactly-equal values in a sequence (a "plateau").
/// <paramref name="StartIndex"/> is the index into the sequence that was scanned (not a BarIndex/timestamp -
/// callers translate back to those via the same sequence they passed in).</summary>
internal readonly record struct PlateauRun<T>(T Value, int StartIndex, int Length);

/// <summary>
/// Lot 15.9. AUDIT-ONLY shared descriptive-statistics helpers - plain math, no calibration/optimization,
/// no economic formulas. Percentile/Pearson formulas are copied verbatim from the existing Lot 15.6/15.8
/// StructuralBreakAudit test files for consistency with prior lots' numbers.
/// </summary>
internal static class AuditMath
{
    public static double Pct(int count, int total) => total > 0 ? Math.Round(100.0 * count / total, 3) : 0.0;

    public static double Mean(IReadOnlyList<double> values) => values.Count > 0 ? values.Average() : 0.0;

    public static double Variance(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0.0;
        double mean = values.Average();
        return values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
    }

    public static double StdDev(IReadOnlyList<double> values) => Math.Sqrt(Variance(values));

    public static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0.0;
        if (sorted.Count == 1) return sorted[0];
        double rank = p * (sorted.Count - 1);
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sorted[lower];
        double fraction = rank - lower;
        return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
    }

    public static double Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count != y.Count || x.Count < 2) return double.NaN;
        double meanX = x.Average(), meanY = y.Average();
        double cov = 0.0, varX = 0.0, varY = 0.0;
        for (int i = 0; i < x.Count; i++)
        {
            double dx = x[i] - meanX, dy = y[i] - meanY;
            cov += dx * dy; varX += dx * dx; varY += dy * dy;
        }
        if (varX <= 0.0 || varY <= 0.0) return double.NaN;
        return cov / Math.Sqrt(varX * varY);
    }

    /// <summary>1-based rank transform with average ranks on ties (standard Spearman convention).</summary>
    public static double[] RankTransform(IReadOnlyList<double> values)
    {
        int n = values.Count;
        int[] order = Enumerable.Range(0, n).OrderBy(i => values[i]).ToArray();
        var ranks = new double[n];
        int idx = 0;
        while (idx < n)
        {
            int j = idx;
            while (j + 1 < n && values[order[j + 1]] == values[order[idx]]) j++;
            double avgRank = (idx + j) / 2.0 + 1.0;
            for (int k = idx; k <= j; k++) ranks[order[k]] = avgRank;
            idx = j + 1;
        }
        return ranks;
    }

    public static double Spearman(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count != y.Count || x.Count < 2) return double.NaN;
        return Pearson(RankTransform(x), RankTransform(y));
    }

    /// <summary>Maximal runs of consecutive, exactly-equal values (EqualityComparer&lt;T&gt;.Default) in
    /// the given sequence, in order. Used for every "plateau"/"run length" measurement in this audit
    /// (Confidence plateaus, BreakCount plateaus, Agreement category durations, StableValue state
    /// diversity) - one generic implementation, one definition, reused everywhere.</summary>
    public static List<PlateauRun<T>> ComputePlateaus<T>(IReadOnlyList<T> values)
    {
        var comparer = EqualityComparer<T>.Default;
        var runs = new List<PlateauRun<T>>();
        int i = 0;
        while (i < values.Count)
        {
            T v = values[i];
            int start = i;
            int j = i + 1;
            while (j < values.Count && comparer.Equals(values[j], v)) j++;
            runs.Add(new PlateauRun<T>(v, start, j - start));
            i = j;
        }
        return runs;
    }
}
