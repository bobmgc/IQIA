using System;
using System.Net.Http;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Backtest.Risk;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Risk;
using Xunit;
using IQIAIndicator.Tests.BacktestTests.Yahoo;

namespace IQIAIndicator.Tests.BacktestTests.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8, brief §19). INTEGRATION / NETWORK - same MES=F/M5/45-day recipe as every prior
/// lot's own Yahoo integration test. Runs the FULL chain (Signal -&gt; Measurement -&gt; Execution -&gt; Risk -&gt;
/// Sizing -&gt; Cost -&gt; PnL -&gt; Equity) once through <see cref="BacktestEngine.RunFullBacktestWithRisk"/>.
///
/// Brief §19 explicitly forbids freezing a numeric assertion that depends on <c>DateTime.UtcNow</c> - the
/// 45-day window is rolling (documented and empirically proven in the Lot 14.7 report: the same untouched
/// Lot 14.6 Yahoo test produces a different GrossPnL/bar-count every time it is re-run). This test therefore
/// asserts ONLY the invariants brief §20 requires, never a fixed PnL/equity number.
/// </summary>
public sealed class RiskYahooIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public RiskYahooIntegrationTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.01m, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Integration_Network_YahooMesFiveMinuteBars_RiskConstraintsAreRespectedThroughoutThePipeline()
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
                100_000m, Spec(), Policy());

            PnLConfiguration pnlConfig = PnLConfiguration.Create(
                InstrumentPnLSpecification.Create("MES", priceUnitValue: 5m, currency: "USD"),
                quantity: 1, startingCapital: 100000m);

            BacktestRiskConfiguration riskConfig = BacktestRiskConfiguration.Create(
                true, RiskDistanceConfiguration.FromTicks(ticks: 4m, tickSize: 0.25m), maxExposure: 50000m);

            BacktestFullResultWithRisk result = new BacktestEngine().RunFullBacktestWithRisk(
                scenario, warmupBars: 128,
                MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 }),
                ExecutionConfiguration.Create(10),
                pnlConfig, ExecutionCostConfiguration.Disabled(), riskConfig);

            Assert.Equal(series.Count, result.ExecutionResult.Positions.Count);
            Assert.True(result.RiskResult.RequestedCount > 0, "Expected at least one Closed position for a 45-day MES M5 series.");

            int checkedCount = 0;
            foreach (var outcome in result.RiskResult.Outcomes)
            {
                if (!outcome.RiskEvaluation.IsAllowed)
                    continue;

                checkedCount++;

                // Brief §19/§20: invariants only - never a fixed PnL/equity number, since the Yahoo window
                // is rolling (see this lot's report and the Lot 14.7 report's own documented finding).
                Assert.True(outcome.RiskEvaluation.AllowedQuantity <= outcome.RiskEvaluation.RequestedQuantity);
                Assert.True(outcome.RiskEvaluation.AllowedQuantity <= 50); // Spec().MaxQuantity
                Assert.True(outcome.RiskEvaluation.TotalEstimatedRisk <= outcome.RiskEvaluation.MaximumAllowedRisk);
                if (outcome.RiskEvaluation.MaxExposure is decimal maxExposure)
                    Assert.True(outcome.RiskEvaluation.Exposure <= maxExposure);

                // Pipeline coherence: Requested -> Risk -> Execution -> Cost -> PnL never disagree on quantity.
                Assert.NotNull(outcome.CostResult);
                Assert.Equal(outcome.RiskEvaluation.AllowedQuantity, outcome.CostResult!.Quantity);
            }

            Assert.True(checkedCount > 0, "Expected at least one ALLOWED position to verify invariants against.");

            _output.WriteLine(
                $"Yahoo dataset: ticker=MES=F, timeframe=M5, from={from:O}, to={to:O}, bars={series.Count}, " +
                $"fingerprint={HistoricalSeriesFingerprint.Compute(series)}.");
            _output.WriteLine(
                $"Risk: RequestedCount={result.RiskResult.RequestedCount}, AllowedCount={result.RiskResult.AllowedCount}, " +
                $"RejectedCount={result.RiskResult.RejectedCount}, FinalNetPnL={result.RiskResult.FinalNetPnL}, " +
                $"FinalEquity={result.RiskResult.FinalEquity}, MaximumDrawdown={result.RiskResult.MaximumDrawdown}.");
            _output.WriteLine(
                "Hash scope (documented, brief §19): RiskResult.DeterministicHash covers every Outcome's " +
                "Status/Direction/IsAllowed/RequestedQuantity/AllowedQuantity/Reason/NetPnL/EquityBefore/" +
                "EquityAfter/Drawdown, in original position order - see RiskResultFingerprint. Deliberately NOT " +
                "compared against a fixed value here: the 45-day Yahoo window is rolling, so bar count/positions/" +
                "PnL legitimately differ run to run.");
            _output.WriteLine($"RiskResult.DeterministicHash={result.RiskResult.DeterministicHash}.");
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException;
}
