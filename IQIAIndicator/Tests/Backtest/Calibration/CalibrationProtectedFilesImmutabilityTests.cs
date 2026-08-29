using System;
using System.IO;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §57/§58/§61 - "PRODUCTION IMMUTABILITY"/"RISK IMMUTABILITY"). This lot's
/// calibration framework never modifies production code (brief §43) - this is the tripwire proving it, not
/// by diffing git history (out of scope for a unit test), but by asserting the exact protected files still
/// declare the exact signatures the brief names, most importantly the untouched
/// <c>AmbiguityGateThreshold = 0.95</c> constant (brief §48: "la valeur production 0.95 reste inchangée").
/// Any future edit to one of these files that removes/renames the checked declaration will fail this test,
/// exactly as intended - it is a canary, not a full-file hash (a hash would also break on an authorized,
/// unrelated formatting change to the same file, which is not what this lot needs to catch).
/// </summary>
public sealed class CalibrationProtectedFilesImmutabilityTests
{
    private static string MainProjectDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;

        if (dir is null)
            throw new InvalidOperationException("Could not locate IQIAIndicator.Tests.csproj to resolve the main project's directory.");

        string mainProjectDir = Path.GetFullPath(Path.Combine(dir, ".."));
        if (!File.Exists(Path.Combine(mainProjectDir, "IQIAIndicator.csproj")))
            throw new InvalidOperationException($"Expected to find IQIAIndicator.csproj at '{mainProjectDir}'.");

        return mainProjectDir;
    }

    private static void AssertContains(string relativePath, string mustContain)
    {
        string path = Path.Combine(MainProjectDirectory(), relativePath);
        Assert.True(File.Exists(path), $"Protected file not found: {path}.");

        string content = File.ReadAllText(path);
        Assert.Contains(mustContain, content);
    }

    [Fact]
    public void AmbiguityGateThreshold_IsStillProductionValue_0_95_NeverRecalibratedByThisLot()
    {
        AssertContains(Path.Combine("Engine", "EntryTrigger", "EntryTriggerBuilder.cs"), "AmbiguityGateThreshold = 0.95");
    }

    [Fact]
    public void RiskEngineFamily_FilesAreIntact()
    {
        AssertContains(Path.Combine("Engine", "Risk", "RiskEngine.cs"), "public sealed class RiskEngine");
        AssertContains(Path.Combine("Engine", "Risk", "RiskPolicy.cs"), "public sealed record RiskPolicy");
        AssertContains(Path.Combine("Engine", "Risk", "InstrumentRiskSpecification.cs"), "public sealed record InstrumentRiskSpecification");
        AssertContains(Path.Combine("Engine", "Risk", "RiskEngineRequest.cs"), "public sealed record RiskEngineRequest");
        AssertContains(Path.Combine("Engine", "Risk", "RiskAssessment.cs"), "public sealed record RiskAssessment");
    }

    [Fact]
    public void DecisionAndTradePlanFamily_FilesAreIntact()
    {
        AssertContains(Path.Combine("Engine", "Decision", "Arbitration", "DecisionArbitrator.cs"), "public sealed class DecisionArbitrator");
        AssertContains(Path.Combine("Engine", "EntryTrigger", "EntryTriggerBuilder.cs"), "public sealed class EntryTriggerBuilder");
        AssertContains(Path.Combine("Engine", "TradePlan", "TradePlanBuilder.cs"), "public sealed class TradePlanBuilder");
    }

    [Fact]
    public void PipelineEngineFamily_FilesAreIntact()
    {
        AssertContains(Path.Combine("Engine", "Regime", "RegimeEngine.cs"), "public sealed class RegimeEngine");
        AssertContains(Path.Combine("Engine", "Fusion", "EvidenceFusionEngine.cs"), "public sealed class EvidenceFusionEngine");
        AssertContains(Path.Combine("Engine", "Fusion", "State", "FusionStateManager.cs"), "public sealed class FusionStateManager");
        AssertContains(Path.Combine("Engine", "Decision", "Core", "DecisionEngine.cs"), "public sealed class DecisionEngine");
        AssertContains(Path.Combine("Engine", "Signal", "SignalEngine.cs"), "public sealed class SignalEngine");
        AssertContains(Path.Combine("Engine", "Entry", "EntryEngine.cs"), "public sealed class EntryEngine");
        AssertContains(Path.Combine("Engine", "EntryTrigger", "EntryTriggerEngine.cs"), "public sealed class EntryTriggerEngine");
    }

    [Fact]
    public void DataSourceAndContextFactory_FilesAreIntact()
    {
        AssertContains(Path.Combine("Core", "MarketData", "Yahoo", "YahooHistoricalBarSource.cs"), "public sealed class YahooHistoricalBarSource");
        AssertContains(Path.Combine("Core", "MarketContextFactory.cs"), "public static class MarketContextFactory");
    }

    [Fact]
    public void BacktestEngine_StillExposesEveryPriorLotsEntryPoint_NeverModifiedByThisLot()
    {
        AssertContains(Path.Combine("Backtest", "BacktestEngine.cs"), "public BacktestFoundationResult Run(");
        AssertContains(Path.Combine("Backtest", "BacktestEngine.cs"), "public BacktestSignalPipelineResult RunSignalPipeline(");
        AssertContains(Path.Combine("Backtest", "BacktestEngine.cs"), "public BacktestFullResultWithRisk RunFullBacktestWithRisk(");
    }
}
