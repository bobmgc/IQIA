using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Risk;
using Xunit;
using DecisionRules = IQIAIndicator.Engine.Decision.Rules;
using FusionEngine = IQIAIndicator.Engine.Fusion.EvidenceFusionEngine;
using IQIAIndicator.Tests.BacktestTests.Yahoo;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 15.8, brief §non-régression - BLOCKING acceptance criterion). On the same real Yahoo
/// MES M5 dataset used throughout Lot 15.x, compares <see cref="MarketState"/> (<see cref="DecisionResult.Winner"/>)
/// bar-by-bar BEFORE (fusion rule list WITHOUT <see cref="StructuralBreakEvidenceRule"/> - i.e. the exact
/// pre-Lot-15.8 production rule list) and AFTER (the real, unmodified
/// <see cref="BacktestEngine.RunSignalPipeline"/>, which now includes it). Since no
/// <see cref="DecisionRules.IDecisionRule"/> reads <see cref="FusionDimension.StructuralBreak"/> (verified
/// by direct inspection of all five files under Engine/Decision/Rules/ - none references that enum value),
/// these two must be IDENTICAL for every single processed bar. A single differing bar is a real bug to be
/// reported, not silently patched here (per this lot's brief: "si un test semble exiger une modification,
/// remonte le problème").
///
/// AFTER comes directly from the real pipeline's own <see cref="BacktestSignalResult.Decision"/>. BEFORE is
/// reconstructed with a separate, freshly-constructed four-rule <see cref="FusionEngine"/> (the exact
/// pre-Lot-15.8 rule list) + <see cref="FusionStateManager"/> + <see cref="DecisionEngine"/> (the same five
/// decision rules, same order, that <see cref="BacktestEngine.RunSignalPipeline"/> itself constructs),
/// fed from each processed bar's own already-computed, already-look-ahead-proven <see cref="EvidenceSet"/>
/// (<c>bar.Regime</c>) - the Regime stage runs identically regardless of which Fusion rules exist
/// downstream, so re-running it is unnecessary (same reasoning as
/// <c>StructuralBreakEvidenceAblationLot152Tests</c>' own parallel-walk technique).
/// </summary>
public sealed class StructuralBreakRegressionTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public StructuralBreakRegressionTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    /// <summary>The exact pre-Lot-15.8 production fusion rule list (BacktestEngine.RunSignalPipeline before
    /// this lot's one-line addition) - StructuralBreakEvidenceRule deliberately excluded.</summary>
    private static FusionEngine BuildBeforeFusionEngine() => new(new IFusionRule[]
    {
        new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new RandomWalkRule()
    });

    /// <summary>The same five decision rules, same order, BacktestEngine.RunSignalPipeline constructs.</summary>
    private static DecisionEngine BuildDecisionEngine() => new(new DecisionRules.IDecisionRule[]
    {
        new DecisionRules.StableRangeRule(),
        new DecisionRules.TrendingRule(),
        new DecisionRules.MeanRevertingRule(),
        new DecisionRules.StructuralBreakRule(),
        new DecisionRules.RandomWalkRule()
    });

    [Fact]
    public void Integration_Network_MarketStateWinner_IsBitIdentical_BeforeAndAfterStructuralBreakEvidenceRule()
    {
        try
        {
            HistoricalSeries series = _yahoo.Require();
            Assert.True(series.Count > 0);

            _output.WriteLine("=== DATASET IDENTITY (Lot 15.8 regression) ===");
            _output.WriteLine($"Symbol={series.Symbol}, Timeframe={series.TimeFrame}, BarCount={series.Count}");
            _output.WriteLine($"Range: {series.FirstTimestamp:O} .. {series.LastTimestamp:O}");

            const int warmupBars = 128;
            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT15.8-REGRESSION", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            // AFTER: the real, unmodified production pipeline (includes StructuralBreakEvidenceRule).
            BacktestSignalPipelineResult afterResult = new BacktestEngine().RunSignalPipeline(scenario, warmupBars);
            Assert.Equal(0, afterResult.ExceptionCount);
            _output.WriteLine($"AFTER: BarsProcessed={afterResult.BarsProcessed}, ReadyBars={afterResult.ReadyBars}, ExceptionCount={afterResult.ExceptionCount}");

            // BEFORE: independent four-rule Fusion walk + fresh FusionStateManager + fresh DecisionEngine,
            // fed from AFTER's own already-computed EvidenceSet per bar (Regime is unaffected by which
            // Fusion rules exist downstream).
            FusionEngine beforeFusion = BuildBeforeFusionEngine();
            var beforeFusionState = new FusionStateManager();
            DecisionEngine beforeDecision = BuildDecisionEngine();

            int comparedBars = 0;
            int mismatches = 0;
            var mismatchDetails = new List<string>();

            foreach (BacktestSignalResult bar in afterResult.Bars)
            {
                if (bar.Status is BacktestSignalStatus.Rejected or BacktestSignalStatus.Exception) continue;
                if (bar.Regime is null || bar.Decision is null) continue;

                FusionResult beforeRaw = beforeFusion.Fuse(new FusionContext
                {
                    Evidence = bar.Regime, Timestamp = bar.Timestamp, Symbol = series.Symbol, TimeFrame = series.TimeFrame, EvaluationId = Guid.Empty
                });
                FusionSnapshot beforeSnapshot = beforeFusionState.Update(beforeRaw, bar.Timestamp);
                DecisionResult beforeDecisionResult = beforeDecision.Evaluate(new DecisionContext
                {
                    FusionResult = beforeSnapshot.StableResult, Evidence = bar.Regime
                });

                comparedBars++;
                MarketState afterWinner = bar.Decision.Winner;
                MarketState beforeWinner = beforeDecisionResult.Winner;

                if (afterWinner != beforeWinner)
                {
                    mismatches++;
                    if (mismatchDetails.Count < 25)
                        mismatchDetails.Add($"Bar {bar.BarIndex} ({bar.Timestamp:O}): BEFORE={beforeWinner}, AFTER={afterWinner}");
                }
            }

            _output.WriteLine($"ComparedBars={comparedBars}, Mismatches={mismatches}");
            if (mismatches > 0)
            {
                _output.WriteLine("=== MISMATCH DETAILS (first 25) ===");
                foreach (string detail in mismatchDetails)
                    _output.WriteLine(detail);
            }

            Assert.True(comparedBars > 0, "Expected at least one comparable bar to make this regression test meaningful.");
            Assert.Equal(0, mismatches);
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException;
}
