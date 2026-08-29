namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13 (brief §12). SyntheticSeriesCatalog.VarianceBreak keeps its break index (length/2) as
/// a private local - it is not exposed by the generator. Sprint 15.12 re-derived the same value
/// externally ("recovered from the generator itself... not invented") rather than modifying the
/// generator; this sprint follows the same approach rather than touching Tests/GoldenDatasets/
/// (outside this sprint's allowed paths). Must be kept in sync by hand with
/// SyntheticSeriesCatalog.VarianceBreak's own `int breakIndex = length / 2;` if that ever changes.
/// </summary>
public static class VarianceBreakInfo
{
    public static int BreakIndex(int seriesLength) => seriesLength / 2;
}
