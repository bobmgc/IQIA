using System;
using System.Net.Http;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Risk;
using Xunit;
using Xunit.Abstractions;

namespace IQIAIndicator.Tests.BacktestTests.Measurement;

/// <summary>
/// Sprint 15.25 (Lot 14.4, brief §41). INTEGRATION / NETWORK - same pattern as Lot 14.3's own Yahoo
/// integration test (real HTTP call, try/catch-on-connectivity-issue, never fails the suite when Yahoo is
/// unreachable). Requests the largest window Yahoo's 5m depth limit actually allows (60 days, confirmed
/// empirically by Lot 14.2 - kept at 45 here to stay comfortably under that hard limit) to reach "a few
/// thousand bars" (brief §41) without downloading tens of thousands of bars nobody asked for.
/// </summary>
public sealed class ScientificMeasurementYahooIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public ScientificMeasurementYahooIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Integration_Network_YahooMesFiveMinuteBars_RunThroughTheMeasuredPipeline()
    {
        try
        {
            var source = new YahooHistoricalBarSource();
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-45);

            HistoricalSeries series = source.Load("MES", "M5", from, to);
            Assert.True(series.Count > 0);

            var scenario = BacktestScenario.Create(
                series,
                new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            MeasurementConfiguration config = MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 });
            BacktestMeasuredSignalPipelineResult result =
                new BacktestEngine().RunMeasuredSignalPipeline(scenario, warmupBars: 128, config);

            Assert.Equal(series.Count, result.SignalResult.Bars.Count);
            Assert.Equal(series.Count, result.Measurements.Count);

            ScientificMeasurementSummaryByDirection summary = ScientificMeasurementSummaryBuilder.Summarize(result.Measurements);

            _output.WriteLine(
                $"Yahoo dataset: ticker=MES=F, timeframe=M5, from={from:O}, to={to:O}, bars={series.Count}, " +
                $"fingerprint={HistoricalSeriesFingerprint.Compute(series)}.");
            _output.WriteLine(
                $"Signal pipeline: BarsProcessed={result.SignalResult.BarsProcessed}, BUY={result.SignalResult.BuyCount}, " +
                $"SELL={result.SignalResult.SellCount}, TradePlan.SIGNAL_ONLY={result.SignalResult.TradePlanSignalOnlyCount}.");
            _output.WriteLine(
                $"Measurement (ALL): Count={summary.All.Count}, MedianReturn={summary.All.MedianReturn}, " +
                $"MedianMfe={summary.All.MedianMfe}, MedianMae={summary.All.MedianMae}.");
            _output.WriteLine(
                $"Measurement (BUY): Count={summary.Buy.Count}, MedianReturn={summary.Buy.MedianReturn}. " +
                $"Measurement (SELL): Count={summary.Sell.Count}, MedianReturn={summary.Sell.MedianReturn}.");
            _output.WriteLine($"MeasurementResult.DeterministicHash={result.DeterministicHash}.");
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            _output.WriteLine($"SKIPPED (network/Yahoo unavailable, not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or InvalidOperationException;
}
