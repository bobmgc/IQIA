using System;
using System.IO;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.RealMarket;

/// <summary>Sprint 15.16. Resolves the RealMarket output directory relative to the Tests project
/// regardless of the test runner's working directory - same walk-up-to-csproj-marker approach as
/// CampaignOutputPaths.ResolveOutputDirectory(), pointed at RealMarket/ instead of Output/, so real-data
/// audit artifacts are never written into (or confused with) the synthetic campaign's Output/ folder.</summary>
internal static class RealMarketOutputPaths
{
    public static string ResolveOutputDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate IQIAIndicator.Tests.csproj to resolve the RealMarket output directory.");
        }

        string output = Path.Combine(dir, "Research", "StopLossCalibration", "RealMarket");
        Directory.CreateDirectory(output);
        return output;
    }
}
