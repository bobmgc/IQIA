using System;
using System.Net.Http;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Risk;
using Xunit;
using Xunit.Abstractions;

namespace IQIAIndicator.Tests.BacktestTests.Execution;

/// <summary>
/// Sprint 15.25 (Lot 14.5, brief §40). INTEGRATION / NETWORK - same pattern as Lot 14.3/14.4's own Yahoo
/// integration tests. Reuses the EXACT same parameters Lot 14.4 validated (MES=F, M5, 45-day window) -
/// Yahoo has no historical-snapshot API, so a fresh pull with IDENTICAL parameters is the closest
/// practical "reuse of the already-validated series" (brief §40); the exact bar content will differ
/// slightly from Lot 14.4's own pull since both anchor on a rolling "now", documented here rather than
/// assumed to be bit-identical to that lot's captured numbers.
/// </summary>
public sealed class ExecutionYahooIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public ExecutionYahooIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Integration_Network_YahooMesFiveMinuteBars_RunThroughTheFullSimulation()
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

            BacktestSimulationResult result = new BacktestEngine().RunSimulation(
                scenario, warmupBars: 128,
                MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 }),
                ExecutionConfiguration.Create(10));

            Assert.Equal(series.Count, result.SignalResult.Bars.Count);
            Assert.Equal(series.Count, result.Measurements.Count);
            Assert.Equal(series.Count, result.ExecutionResult.Positions.Count);
            Assert.Equal(result.ExecutionResult.TotalCount,
                result.ExecutionResult.ClosedCount + result.ExecutionResult.NotExecutableCount +
                result.ExecutionResult.InvalidEntryCount + result.ExecutionResult.InsufficientFutureDataCount +
                result.ExecutionResult.InvalidExitCount);

            // Determinism, re-run once more against the SAME already-downloaded series.
            BacktestExecutionResult rerun = ExecutionSimulator.SimulateAll(series, result.SignalResult.Bars, ExecutionConfiguration.Create(10));
            Assert.Equal(result.ExecutionResult.DeterministicHash, rerun.DeterministicHash);

            _output.WriteLine(
                $"Yahoo dataset: ticker=MES=F, timeframe=M5, from={from:O}, to={to:O}, bars={series.Count}, " +
                $"fingerprint={HistoricalSeriesFingerprint.Compute(series)}.");
            _output.WriteLine(
                $"Execution: TotalCount={result.ExecutionResult.TotalCount}, Closed={result.ExecutionResult.ClosedCount}, " +
                $"BUY={result.ExecutionResult.BuyCount}, SELL={result.ExecutionResult.SellCount}, " +
                $"NotExecutable={result.ExecutionResult.NotExecutableCount}, InvalidEntry={result.ExecutionResult.InvalidEntryCount}, " +
                $"InsufficientFutureData={result.ExecutionResult.InsufficientFutureDataCount}, InvalidExit={result.ExecutionResult.InvalidExitCount}.");
            _output.WriteLine($"ExecutionResult.DeterministicHash={result.ExecutionResult.DeterministicHash}.");
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            _output.WriteLine($"SKIPPED (network/Yahoo unavailable, not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or InvalidOperationException;
}
