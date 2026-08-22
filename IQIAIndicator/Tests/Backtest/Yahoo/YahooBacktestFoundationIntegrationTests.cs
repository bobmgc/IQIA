using System;
using System.Collections.Generic;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 14.2, brief §21/§22). Yahoo -&gt; HistoricalSeries -&gt; BacktestEngine -&gt;
/// BacktestFoundationResult, end to end, but fixture-driven (deterministic, no network - brief §5). This
/// is the PRIMARY proof the wiring works; YahooNetworkIntegrationTests.cs additionally exercises the same
/// path against the real Yahoo endpoint when internet is available, as an optional extra.
///
/// Per brief §21, this suite does NOT exercise RiskEngine/Decision/Entry/TradePlan - BacktestEngine
/// (Lot 14.1) does not call any of them yet, and this lot does not change that.
/// </summary>
public sealed class YahooBacktestFoundationIntegrationTests
{
    private static readonly DateTime T0 = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // 10 bars, 5 minutes apart, no gaps - simple, real-shaped fixture.
    private const string TenBars =
        """
        {"chart":{"result":[{"meta":{"symbol":"MES=F"},"timestamp":[1735689600,1735689900,1735690200,1735690500,1735690800,1735691100,1735691400,1735691700,1735692000,1735692300],"indicators":{"quote":[{"open":[100,100.25,100.5,100.4,100.6,100.7,100.55,100.65,100.8,100.75],"high":[100.5,100.5,100.7,100.6,100.8,100.9,100.75,100.85,101,100.95],"low":[99.75,100,100.3,100.2,100.4,100.5,100.35,100.45,100.6,100.55],"close":[100.25,100.5,100.4,100.6,100.7,100.55,100.65,100.8,100.75,100.9],"volume":[10,20,15,25,18,22,17,19,21,23]}]}}],"error":null}}
        """;

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static BacktestScenario ScenarioFor(HistoricalSeries series)
    {
        var window = new BacktestWindow("YAHOO_FOUNDATION", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1));
        return BacktestScenario.Create(series, window, 50_000m, Spec(), Policy());
    }

    [Fact]
    public void YahooSeries_ThroughBacktestEngine_ProducesAValidFoundationResult()
    {
        var fake = new FakeYahooChartClient(TenBars);
        var yahoo = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        HistoricalSeries series = yahoo.Load("MES", "M5", T0, T0.AddDays(1));
        BacktestScenario scenario = ScenarioFor(series);

        BacktestFoundationResult result = new BacktestEngine().Run(scenario, warmupBars: 3);

        Assert.Equal(10, result.BarsProcessed);
        Assert.Equal(0, result.BarsRejected);
        Assert.Equal(series.FirstTimestamp, result.FirstTimestamp);
        Assert.Equal(series.LastTimestamp, result.LastTimestamp);
        Assert.NotEqual(default, result.FirstTimestamp);
        Assert.False(string.IsNullOrWhiteSpace(result.DeterministicHash));
    }

    [Fact]
    public void YahooSeries_ThroughBacktestEngine_IsDeterministic_AcrossRepeatedRuns()
    {
        var fake = new FakeYahooChartClient(TenBars, TenBars);
        var yahoo = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        HistoricalSeries seriesA = yahoo.Load("MES", "M5", T0, T0.AddDays(1));
        HistoricalSeries seriesB = yahoo.Load("MES", "M5", T0, T0.AddDays(1));

        BacktestFoundationResult resultA = new BacktestEngine().Run(ScenarioFor(seriesA), warmupBars: 3);
        BacktestFoundationResult resultB = new BacktestEngine().Run(ScenarioFor(seriesB), warmupBars: 3);

        Assert.Equal(resultA, resultB);
    }

    [Fact]
    public void YahooSeries_HasADeterministicFingerprint_ComputableAfterLoad()
    {
        var fake = new FakeYahooChartClient(TenBars);
        var yahoo = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        HistoricalSeries series = yahoo.Load("MES", "M5", T0, T0.AddDays(1));

        string fingerprint = HistoricalSeriesFingerprint.Compute(series);

        Assert.Equal(64, fingerprint.Length);
    }

    /// <summary>
    /// Brief §22: proves YahooHistoricalBarSource does not change how BacktestEngine/MarketContextFactory
    /// behave. A HistoricalSeries built by hand with IDENTICAL bar values (same Timestamp/OHLCV, only
    /// Provider differs) must produce BIT-IDENTICAL per-bar MarketContext/EvidenceSet through the exact
    /// same, unmodified BacktestEngine - the Yahoo path introduces no special-casing anywhere downstream.
    /// </summary>
    [Fact]
    public void YahooDerivedSeries_And_ManuallyBuiltEquivalentSeries_ProduceIdenticalEngineOutput()
    {
        var fake = new FakeYahooChartClient(TenBars);
        var yahoo = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);
        HistoricalSeries yahooSeries = yahoo.Load("MES", "M5", T0, T0.AddDays(1));

        var manualBars = new List<HistoricalBar>();
        foreach (HistoricalBar bar in yahooSeries.Bars)
            manualBars.Add(bar); // same values, different series identity below
        HistoricalSeries manualSeries = HistoricalSeries.Create("MES", "M5", "UTC", "ManualEquivalent", manualBars);

        var yahooObserver = new CapturingBarObserver();
        var manualObserver = new CapturingBarObserver();

        new BacktestEngine().Run(ScenarioFor(yahooSeries), warmupBars: 3, yahooObserver);
        new BacktestEngine().Run(ScenarioFor(manualSeries), warmupBars: 3, manualObserver);

        Assert.Equal(yahooObserver.ProcessedBars.Count, manualObserver.ProcessedBars.Count);
        for (int i = 0; i < yahooObserver.ProcessedBars.Count; i++)
        {
            var a = yahooObserver.ProcessedBars[i];
            var b = manualObserver.ProcessedBars[i];

            Assert.Equal(a.IsWarmup, b.IsWarmup);
            Assert.Equal(a.Context.Price, b.Context.Price);
            Assert.Equal(a.Context.Volume, b.Context.Volume);
            Assert.Equal(a.Context.Clock.CurrentTime, b.Context.Clock.CurrentTime);
            Assert.Equal(a.Context.Clock.ElapsedMinutes, b.Context.Clock.ElapsedMinutes);
            Assert.Equal(a.Context.Execution.CurrentBar, b.Context.Execution.CurrentBar);
            Assert.Equal(a.Evidence.Adf?.IsValid, b.Evidence.Adf?.IsValid);
            Assert.Equal(a.Evidence.Adf?.Statistic, b.Evidence.Adf?.Statistic);
        }
    }
}
