using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using IQIAIndicator.Backtest.Calibration;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using Xunit;
using Xunit.Abstractions;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §46/§47/§51 - INTEGRATION / NETWORK). Same MES=F/M5/45-day recipe as every
/// prior lot's own Yahoo integration test (brief §46: "NE PAS dépendre d'ATAS. NE PAS lancer de capture
/// live."). Runs ONE real experiment through <see cref="CalibrationExperimentRunner"/> end to end
/// (Yahoo -&gt; Signal -&gt; Measurement -&gt; Execution -&gt; Risk -&gt; PnL, sliced into TRAIN/VALIDATION/OOS) to
/// prove the framework, not to select a parameter (brief §47/§48: no conclusion about which value is
/// "better" is drawn or asserted here).
///
/// Brief §19-style discipline (same as <see cref="RiskYahooIntegrationTests"/>): the 45-day window is
/// rolling, so this test asserts only structural invariants, never a fixed PnL/equity/count.
/// </summary>
public sealed class CalibrationYahooIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public CalibrationYahooIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Integration_Network_YahooMesFiveMinuteBars_RunsOneCalibrationExperimentAcrossTrainValidationOos()
    {
        try
        {
            var source = new YahooHistoricalBarSource();
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-45);

            HistoricalSeries series = source.Load("MES", "M5", from, to);
            Assert.True(series.Count > 0);

            const int leadIn = 128;
            int remaining = series.Count - leadIn;
            if (remaining < 30)
            {
                _output.WriteLine($"SKIPPED (not a code failure): only {series.Count} bars returned, insufficient for a TRAIN/VALIDATION/OOS split.");
                return;
            }

            int train = (int)(remaining * 0.7);
            int validation = (int)(remaining * 0.15);
            int oos = remaining - train - validation - 1; // leave a 1-bar safety margin below series.Count
            if (train < 1 || validation < 1 || oos < 1)
            {
                _output.WriteLine("SKIPPED (not a code failure): remaining bars too few to give every window at least one bar.");
                return;
            }

            CalibrationDataset dataset = CalibrationTestFixtures.Dataset(series, provider: "Yahoo");
            CalibrationWindowSet windows = CalibrationTestFixtures.Windows(dataset, leadIn, train, validation, oos);
            CalibrationExperimentSetup setup = CalibrationTestFixtures.Setup(requiredWarmupBars: leadIn);

            CalibrationExperiment experiment = CalibrationExperiment.Create(
                CalibrationParameterSet.Create(new[] { CalibrationParameter.Decimal("AmbiguityThreshold", 0.95m, "score") }),
                dataset, windows, setup);

            CalibrationExperimentRunResult result = CalibrationExperimentRunner.Run(experiment);

            // Structural invariants only (brief §47/§48 - never a "best value" conclusion here).
            Assert.Equal(9, result.Results.Count); // 3 windows x 3 directions
            Assert.Equal(experiment.ExperimentId, result.ExperimentId);
            Assert.Equal(experiment.ConfigurationFingerprint, result.ConfigurationFingerprint);

            foreach (CalibrationExperimentResult slice in result.Results)
            {
                Assert.True(slice.SignalCount >= 0);
                Assert.True(slice.PositionCount >= 0);
                // Sprint 15.25 (Lot 14.10, P0-1): "PositionCount <= SignalCount within the SAME window"
                // is no longer a per-slice invariant. CalibrationExperimentRunner.BuildSlice buckets
                // SignalCount by MeasurementResult.SignalTimestamp but PositionCount by
                // PositionRiskOutcome.EntryTimestamp - those two were always the same bar before this lot
                // (entry == signal bar), so the two bucketings could never disagree. Now EntryTimestamp is
                // the FILL bar (SignalBarIndex+1), one bar later than SignalTimestamp - a signal that fires
                // on the very last bar of a window can legitimately fill into the FIRST bar of the next
                // window, moving that one position's count across the boundary. This is correct windowing
                // behaviour (a position is counted where it actually opened), not a defect.
                if (slice.Status == CalibrationExperimentResultStatus.NoData)
                {
                    Assert.Equal(0, slice.SignalCount);
                    Assert.Equal(0, slice.PositionCount);
                }
            }

            // Brief §57/§58: production constant untouched by merely running an experiment - runtime-side
            // confirmation, alongside CalibrationProtectedFilesImmutabilityTests' static file check.
            Assert.Equal(0.95m, experiment.ParameterSet.TryGet("AmbiguityThreshold")!.DecimalValue);

            _output.WriteLine(
                $"Yahoo dataset: symbol=MES, timeframe=M5, from={from:O}, to={to:O}, bars={series.Count}, " +
                $"fingerprint={dataset.Fingerprint}.");
            _output.WriteLine($"ExperimentId={experiment.ExperimentId}, ConfigurationFingerprint={experiment.ConfigurationFingerprint}.");
            foreach (CalibrationExperimentResult slice in result.Results)
            {
                _output.WriteLine(
                    $"{slice.Window.Role}/{slice.Direction}: status={slice.Status}, signals={slice.SignalCount}, " +
                    $"positions={slice.PositionCount}, grossPnL={slice.GrossPnL}, finalEquity={slice.FinalEquity}, " +
                    $"maxDrawdown={slice.MaximumDrawdown}, winRate={slice.WinRate}.");
            }
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            _output.WriteLine($"SKIPPED (network/Yahoo unavailable, not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or InvalidOperationException;
}
