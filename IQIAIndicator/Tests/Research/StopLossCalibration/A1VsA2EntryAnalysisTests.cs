using System;
using System.IO;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

public static class A1VsA2EntryAnalysisTests
{
    public static void RunAll()
    {
        (string comparisonPath, string ratioPath, int entryCount) = A1VsA2EntryAnalysis.RunAndWrite();

        Assert(entryCount > 0, "Entry-level analysis produced zero entries.");
        Assert(File.Exists(comparisonPath), $"Comparison CSV was not written to {comparisonPath}.");
        Assert(File.Exists(ratioPath), $"Ratio CSV was not written to {ratioPath}.");

        // Same population expected as the QDE-012 campaign (Sprint 15.10): 112,662 entries.
        Assert(entryCount == 112662, $"Entry count must match the QDE-012 campaign's population exactly (112662). Actual={entryCount}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
