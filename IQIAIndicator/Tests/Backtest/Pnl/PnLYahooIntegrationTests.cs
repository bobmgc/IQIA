using System;
using System.Net.Http;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Risk;
using Xunit;
using IQIAIndicator.Tests.BacktestTests.Yahoo;

namespace IQIAIndicator.Tests.BacktestTests.Pnl;

/// <summary>
/// Sprint 15.25 (Lot 14.6, brief §47). INTEGRATION / NETWORK - same pattern and same parameters
/// (MES=F, M5, 45-day window) as Lot 14.3/14.4/14.5's own Yahoo integration tests. Runs the FULL chain
/// (Signal -&gt; Measurement -&gt; Execution -&gt; P&amp;L) once through <see cref="BacktestEngine.RunFullBacktest"/>.
/// </summary>
public sealed class PnLYahooIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public PnLYahooIntegrationTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Integration_Network_YahooMesFiveMinuteBars_RunThroughTheFullPnlPipeline()
    {
        try
        {
            HistoricalSeries series = _yahoo.LastDays(45);
            DateTime from = series.FirstTimestamp;
            DateTime to = series.LastTimestamp;
            Assert.True(series.Count > 0);

            var scenario = BacktestScenario.Create(
                series,
                new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            PnLConfiguration pnlConfig = PnLConfiguration.Create(
                InstrumentPnLSpecification.Create("MES", priceUnitValue: 5m, currency: "USD"),
                quantity: 1, startingCapital: 100000m);

            BacktestFullResult result = new BacktestEngine().RunFullBacktest(
                scenario, warmupBars: 128,
                MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 }),
                ExecutionConfiguration.Create(10),
                pnlConfig);

            Assert.Equal(series.Count, result.PnLResult.PositionPnLResults.Count);
            Assert.Equal(result.ExecutionResult.ClosedCount, result.PnLResult.Summary.ClosedCount);
            Assert.Equal(result.ExecutionResult.ClosedCount, result.PnLResult.EquityCurve.Count);

            // Determinism: rebuild the P&L layer alone from the already-computed positions.
            BacktestPnLResult rebuilt = BacktestPnLResultBuilder.Build(
                PositionPnLCalculator.CalculateAll(result.ExecutionResult.Positions, pnlConfig), pnlConfig.StartingCapital);
            Assert.Equal(result.PnLResult.DeterministicHash, rebuilt.DeterministicHash);

            _output.WriteLine(
                $"Yahoo dataset: ticker=MES=F, timeframe=M5, from={from:O}, to={to:O}, bars={series.Count}, " +
                $"fingerprint={HistoricalSeriesFingerprint.Compute(series)}.");
            _output.WriteLine(
                $"P&L (StartingCapital=100000 USD, MES 5 USD/point, Quantity=1): " +
                $"ClosedCount={result.PnLResult.Summary.ClosedCount}, GrossProfit={result.PnLResult.Summary.GrossProfit}, " +
                $"GrossLoss={result.PnLResult.Summary.GrossLoss}, NetGrossPnL={result.PnLResult.Summary.NetGrossPnL}, " +
                $"WinRate={result.PnLResult.Summary.WinRate}, FinalGrossPnL={result.PnLResult.FinalGrossPnL}, " +
                $"MaximumDrawdown={result.PnLResult.MaximumDrawdown}, FinalEquity={result.PnLResult.FinalEquity}.");
            _output.WriteLine(
                $"BUY: Count={result.PnLResult.BuySummary.ClosedCount}, NetGrossPnL={result.PnLResult.BuySummary.NetGrossPnL}. " +
                $"SELL: Count={result.PnLResult.SellSummary.ClosedCount}, NetGrossPnL={result.PnLResult.SellSummary.NetGrossPnL}.");
            _output.WriteLine($"PnLResult.DeterministicHash={result.PnLResult.DeterministicHash}.");
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException;
}
