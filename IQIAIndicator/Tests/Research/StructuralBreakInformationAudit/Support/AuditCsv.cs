using System.Globalization;
using System.Text;
using IQIAIndicator.Backtest;

namespace IQIAIndicator.Tests.Research.StructuralBreakInformationAudit.Support;

/// <summary>
/// Lot 15.9. AUDIT-ONLY CSV/hash plumbing shared by every test class in this folder - copied/adapted from
/// the identical helpers in the existing Lot 15.6/15.8 StructuralBreakAudit test files, so output format
/// stays consistent across lots. Reuses <see cref="BacktestFingerprint.Sha256Hex"/> (already public,
/// production code, read-only) instead of re-implementing SHA-256.
/// </summary>
internal static class AuditCsv
{
    public static string ResolveOutputDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;

        if (dir is null)
            throw new InvalidOperationException("Could not locate IQIAIndicator.Tests.csproj to resolve the StructuralBreakInformationAudit output directory.");

        string outputDir = Path.Combine(dir, "Research", "StructuralBreakInformationAudit", "Output");
        Directory.CreateDirectory(outputDir);
        return outputDir;
    }

    /// <summary>Writes a CSV whose first line is a "# Lot15.9,DatasetFingerprint=...,ExecutedAtUtc=..."
    /// metadata line (brief Section D requirement), followed by the normal header + data rows.</summary>
    public static void WriteCsv(string path, string datasetFingerprint, string[] headers, List<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(FormattableString.Invariant($"# Lot15.9,DatasetFingerprint={datasetFingerprint},ExecutedAtUtc={DateTime.UtcNow:O}"));
        sb.AppendLine(string.Join(",", headers.Select(CsvCell)));
        foreach (object?[] row in rows)
        {
            var padded = new object?[headers.Length];
            for (int i = 0; i < headers.Length; i++)
                padded[i] = i < row.Length ? row[i] : null;
            sb.AppendLine(string.Join(",", padded.Select(CsvCell)));
        }
        File.WriteAllText(path, sb.ToString());
    }

    public static string CsvCell(object? value)
    {
        string s = FormatCell(value);
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            s = "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    public static string FormatCell(object? value) => value switch
    {
        null => "",
        double d => d.ToString("G17", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("O"),
        bool b => b.ToString(),
        _ => value.ToString() ?? ""
    };

    public static void AppendHash(StringBuilder sb, List<object?[]> rows)
    {
        foreach (object?[] row in rows)
            sb.Append(string.Join("|", row.Select(FormatCell))).Append(';');
    }

    public static string Sha256Hex(string s) => BacktestFingerprint.Sha256Hex(s);

    /// <summary>Asserts no NaN/Infinity in any double cell across the given row sets - the one
    /// "structural" invariant every Lot 15.9 report table must satisfy (brief: "PAS de NaN/Infinity").
    /// Correlation cells with fewer than 2 points or zero variance legitimately produce NaN
    /// (Pearson/Spearman are undefined there) - callers pass those separately via
    /// <paramref name="allowNaNPredicate"/> when a specific cell is a documented exception.</summary>
    public static void AssertNoNaNOrInfinity(IEnumerable<object?[]> rows, Func<object?[], int, bool>? allowNaNPredicate = null)
    {
        foreach (object?[] row in rows)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (row[i] is double d && !double.IsFinite(d))
                {
                    bool allowed = allowNaNPredicate?.Invoke(row, i) ?? false;
                    if (!allowed)
                        throw new InvalidOperationException(
                            $"NaN/Infinity found in a report cell (row=[{string.Join(",", row.Select(FormatCell))}], col={i}): {d}");
                }
            }
        }
    }
}
