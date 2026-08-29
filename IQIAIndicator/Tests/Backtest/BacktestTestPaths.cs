using System;
using System.IO;

namespace IQIAIndicator.Tests.BacktestTests;

/// <summary>Sprint 15.25 (Lot 14.1). Locates the already-committed real ATAS capture
/// (Tests/Research/StopLossCalibration/RealMarket/RawCapture/Sprint15_18/) regardless of the test
/// runner's working directory - same walk-up-to-csproj-marker approach as the pre-existing
/// RealMarketOutputPaths.ResolveOutputDirectory() (Tests/Research/StopLossCalibration/RealMarket/), kept
/// as a small, self-contained duplicate here rather than reused across assemblies-internal folders, per
/// the Lot 14.1 brief §6 ("créer un petit adapter plutôt que refactorer massivement").</summary>
internal static class BacktestTestPaths
{
    public static string RealCaptureOhlcvCsv()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;

        if (dir is null)
            throw new InvalidOperationException("Could not locate IQIAIndicator.Tests.csproj to resolve the real-capture fixture path.");

        string path = Path.Combine(
            dir, "Research", "StopLossCalibration", "RealMarket", "RawCapture", "Sprint15_18",
            "ScientificDataset_ES_M5_20260814_165421_ohlcv.csv");

        if (!File.Exists(path))
            throw new FileNotFoundException($"Real-capture fixture not found: {path}.", path);

        return path;
    }
}
