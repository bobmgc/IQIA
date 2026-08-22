using System;
using System.Net.Http;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Risk;
using Xunit;
using Xunit.Abstractions;

namespace IQIAIndicator.Tests.BacktestTests.Pipeline;

/// <summary>
/// Sprint 15.25 (Lot 14.3, brief §25/§26). INTEGRATION / NETWORK - makes a REAL HTTP call to Yahoo
/// Finance, exactly like Lot 14.2's <c>YahooNetworkIntegrationTests</c> (same small 2-day/5m window,
/// same try/catch-on-connectivity-issue pattern, same reason: xunit 2.5.3 here has no dynamic skip). This
/// is a best-effort confidence check that Yahoo -&gt; HistoricalSeries -&gt; BacktestEngine -&gt; the FULL signal
/// pipeline -&gt; TradePlan works end to end against real data; it is not the primary proof of correctness
/// (that is the deterministic, network-free tests elsewhere in this folder). Per brief §27, this test
/// does NOT compare Yahoo's numeric results against any ATAS capture - only that the pipeline traverses
/// every stage without throwing and produces a structurally coherent result.
/// </summary>
public sealed class BacktestSignalPipelineYahooIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public BacktestSignalPipelineYahooIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Integration_Network_YahooMesFiveMinuteBars_RunThroughTheFullSignalPipeline()
    {
        try
        {
            var source = new YahooHistoricalBarSource();
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-2);

            HistoricalSeries series = source.Load("MES", "M5", from, to);
            Assert.True(series.Count > 0);

            var scenario = BacktestScenario.Create(
                series,
                new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);

            Assert.Equal(series.Count, result.BarsProcessed + result.BarsRejected);
            Assert.Equal(0, result.ExceptionCount);
            Assert.Equal(series.Count, result.Bars.Count);

            _output.WriteLine(
                $"OK: {series.Count} MES bars -> BarsProcessed={result.BarsProcessed}, WarmupBars={result.WarmupBars}, " +
                $"ReadyBars={result.ReadyBars}, RegimeDetected={result.RegimeDetectedCount}, BUY={result.BuyCount}, " +
                $"SELL={result.SellCount}, NO_ACTION={result.NoActionCount}, TradePlan.SIGNAL_ONLY={result.TradePlanSignalOnlyCount}, " +
                $"Exceptions={result.ExceptionCount}.");
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            _output.WriteLine($"SKIPPED (network/Yahoo unavailable, not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or InvalidOperationException;
}
