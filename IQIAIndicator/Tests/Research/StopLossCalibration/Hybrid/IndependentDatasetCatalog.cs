using System.Collections.Generic;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13 (brief §3). Sprint 15.12 discovered AR1_phi0.5 == MeanRevertingOu_k0.5 and
/// AR1_phi0.95 == MeanRevertingOu_k0.05 (byte-identical series - same recursion, same seed/noise:
/// MeanRevertingOu's discrete-time coefficient is (1-kappa), which equals phi exactly for these pairs).
/// CampaignDatasetCatalog.cs still lists all 11 names as if independent, unmodified by this sprint (the
/// Sprint 15.10-15.12 harness is not touched). This catalog is a pure labeling/lookup layer: it does
/// not change what CampaignDatasetCatalog generates, it only marks which of its 11 labels are
/// independent observations (9) versus aliases of an independent label (2), so downstream analyses can
/// avoid double-counting them as separate evidence.
/// </summary>
public static class IndependentDatasetCatalog
{
    public static readonly IReadOnlyList<string> IndependentDatasets = new[]
    {
        "WhiteNoise", "RandomWalk", "AR1_phi0.5", "AR1_phi0.95", "Trending",
        "LowVolatility", "HighVolatility", "StructuralBreak", "VarianceBreak"
    };

    /// <summary>Alias label -> canonical independent label it is byte-identical to.</summary>
    public static readonly IReadOnlyDictionary<string, string> AliasDatasets = new Dictionary<string, string>
    {
        ["MeanRevertingOu_k0.5"] = "AR1_phi0.5",
        ["MeanRevertingOu_k0.05"] = "AR1_phi0.95"
    };

    public static bool IsIndependent(string dataset) => !AliasDatasets.ContainsKey(dataset);

    public static string AliasOf(string dataset) => AliasDatasets.TryGetValue(dataset, out string? canonical) ? canonical : "";
}
