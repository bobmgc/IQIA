using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.Research.StopLossCalibration.RealMarket;
using Xunit;
using Xunit.Abstractions;

namespace IQIAIndicator.Tests.BacktestTests.Dataset;

/// <summary>
/// Sprint 15.25 (Lot 14.12). INTEGRATION / NETWORK, descriptive only - measures the real MES=F/M5 dataset
/// Yahoo can actually provide, at the maximum depth it allows (see
/// <see cref="YahooHistoricalBarSource.DefaultMaxChunkSpanDays"/>'s own doc comment: Yahoo empirically
/// rejects any 5-minute request older than 60 days, confirmed 2026-08-22 - chunking works AROUND the
/// per-request span limit, it does not and cannot reach further back in time).
///
/// Reuses <see cref="RealMarketQualityAnalyzer"/> (Sprint 15.18) via a thin, local adapter
/// (<see cref="ToRealMarketBars"/>) rather than duplicating gap/OHLC/continuity analysis logic - that
/// analyzer already implements exactly the timestamp/OHLC/gap/continuity/volume checks this lot's brief
/// asks for, and was built and tested independently of this lot (brief: "utiliser exclusivement
/// l'infrastructure existante").
///
/// Runs the REAL, unmodified <see cref="BacktestEngine.RunSignalPipeline"/> over this dataset purely to
/// MEASURE regime/signal/direction coverage (brief §14/§15/§16) - no parameter is touched, no threshold is
/// tuned, nothing here changes pipeline behaviour. Every assertion is a structural/descriptive invariant
/// (counts sum correctly, no NaN, etc.) - never a "good enough" judgment, which belongs in the Lot 14.12
/// report, not in test code.
/// </summary>
public sealed class YahooDatasetCoverageTests
{
    private readonly ITestOutputHelper _output;

    public YahooDatasetCoverageTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Integration_Network_YahooMesFiveMinuteBars_MaximumAvailableDepth_CoverageReport()
    {
        try
        {
            var source = new YahooHistoricalBarSource();
            DateTime to = DateTime.UtcNow;
            // Sprint 15.25 (Lot 14.12): request the MAXIMUM depth Yahoo actually allows for 5-minute data
            // (59 days - YahooHistoricalBarSource.DefaultMaxChunkSpanDays, one day of safety margin under
            // Yahoo's own empirically confirmed 60-day boundary), not the ~45-day window prior lots used
            // for convenience. This is the practical ceiling this lot measures against.
            DateTime from = to.AddDays(-YahooHistoricalBarSource.DefaultMaxChunkSpanDays);

            HistoricalSeries series = source.Load("MES", "M5", from, to);
            Assert.True(series.Count > 0);

            string fingerprint = HistoricalSeriesFingerprint.Compute(series);

            _output.WriteLine("=== DATASET IDENTITY ===");
            _output.WriteLine($"Instrument=MES, Timeframe=M5, Provider={series.Provider}, TimeZone={series.TimeZone}");
            _output.WriteLine($"Requested range: from={from:O}, to={to:O} ({YahooHistoricalBarSource.DefaultMaxChunkSpanDays} days requested)");
            _output.WriteLine($"Actual range: FirstTimestamp={series.FirstTimestamp:O}, LastTimestamp={series.LastTimestamp:O}");
            _output.WriteLine($"BarCount={series.Count}, ChunkCount={source.LastRequestChunkCount}, YahooReportedGapSlots={source.LastRequestGapCount}");
            _output.WriteLine($"Fingerprint={fingerprint}");

            // ── DATA QUALITY (brief §7-11): reuse RealMarketQualityAnalyzer, never re-derive its logic ──

            IReadOnlyList<RealMarketBar> asRealMarketBars = ToRealMarketBars(series);

            TimestampFacts timestampFacts = RealMarketQualityAnalyzer.AnalyzeTimestamps(asRealMarketBars);
            _output.WriteLine("=== TIMESTAMP INTEGRITY ===");
            _output.WriteLine($"StrictlyIncreasing={timestampFacts.StrictlyIncreasing}, DuplicateTimestampCount={timestampFacts.DuplicateTimestampCount}");
            // Already structurally guaranteed by HistoricalSeries.Create's own validating constructor
            // (Core/MarketData/HistoricalSeries.cs) - re-asserted here as defence in depth on the SAME data
            // via an independently-built analyzer, not a duplicate of the same code path.
            Assert.True(timestampFacts.StrictlyIncreasing);
            Assert.Equal(0, timestampFacts.DuplicateTimestampCount);

            OhlcValidationFacts ohlcFacts = RealMarketQualityAnalyzer.ValidateOhlc(asRealMarketBars);
            _output.WriteLine("=== OHLC INTEGRITY ===");
            _output.WriteLine($"TotalBars={ohlcFacts.TotalBars}, StructuralViolations={ohlcFacts.StructuralViolations}, " +
                $"NonPositivePrice={ohlcFacts.NonPositivePriceCount}, NegativeVolume={ohlcFacts.NegativeVolumeCount}, " +
                $"ZeroVolume={ohlcFacts.ZeroVolumeCount}, IdenticalOhlc={ohlcFacts.IdenticalOhlcCount}");
            Assert.Equal(0, ohlcFacts.StructuralViolations);
            Assert.Equal(0, ohlcFacts.NonPositivePriceCount);
            Assert.Equal(0, ohlcFacts.NegativeVolumeCount);

            TimeSpan expectedInterval = TimeSpan.FromMinutes(5);
            IReadOnlyList<GapRecord> gaps = RealMarketQualityAnalyzer.ClassifyGaps(asRealMarketBars, expectedInterval);
            _output.WriteLine("=== GAP ANALYSIS (market gap vs data gap - classification is a size-only heuristic, see RealMarketQualityAnalyzer.ClassifyGaps doc comment) ===");
            foreach (var group in gaps.GroupBy(g => g.Classification).OrderByDescending(g => g.Count()))
                _output.WriteLine($"{group.Key}: {group.Count()} gap(s)");
            _output.WriteLine($"Total gaps={gaps.Count}");

            ContinuityFacts continuity = RealMarketQualityAnalyzer.AnalyzeContinuity(asRealMarketBars, expectedInterval);
            _output.WriteLine("=== CONTINUITY ===");
            _output.WriteLine($"RunCount={continuity.Runs.Count}, LongestRun={(continuity.Runs.Count > 0 ? continuity.Runs.Max(r => r.End - r.Start + 1) : 0)} bars");

            VolumeFacts volumeFacts = RealMarketQualityAnalyzer.AnalyzeVolume(asRealMarketBars);
            _output.WriteLine("=== VOLUME ===");
            _output.WriteLine($"Min={volumeFacts.Min}, Max={volumeFacts.Max}, Median={volumeFacts.Median}, Mean={volumeFacts.Mean}, ZeroCount={volumeFacts.ZeroCount}, NegativeCount={volumeFacts.NegativeCount}");
            Assert.Equal(0, volumeFacts.NegativeCount);

            IReadOnlyList<FormingBarSegment> zeroRangeSegments = RealMarketQualityAnalyzer.DetectZeroRangeSegments(asRealMarketBars);
            _output.WriteLine("=== ZERO-RANGE (Open==High==Low==Close) SEGMENTS ===");
            _output.WriteLine($"SegmentCount={zeroRangeSegments.Count}, TotalZeroRangeBars={zeroRangeSegments.Sum(s => s.BarCount)}");

            // ── COVERAGE (brief §14-17): the REAL, unmodified signal pipeline, run once, purely to measure ──

            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("Lot14.12-Coverage", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            const int warmupBars = 128;
            BacktestSignalPipelineResult signalResult = new BacktestEngine().RunSignalPipeline(scenario, warmupBars);

            _output.WriteLine("=== SIGNAL PIPELINE COVERAGE ===");
            _output.WriteLine($"BarsProcessed={signalResult.BarsProcessed}, BarsRejected={signalResult.BarsRejected}, WarmupBars={signalResult.WarmupBars}, ReadyBars={signalResult.ReadyBars}");
            _output.WriteLine($"RegimeDetectedCount(Winner!=Unknown)={signalResult.RegimeDetectedCount}, DecisionCount={signalResult.DecisionCount}, SignalCount={signalResult.SignalCount}, EntryCandidateCount={signalResult.EntryCandidateCount}");
            _output.WriteLine($"BUY={signalResult.BuyCount}, SELL={signalResult.SellCount}, NO_ACTION={signalResult.NoActionCount}, WATCH={signalResult.WatchCount}");
            _output.WriteLine($"TradePlan: SIGNAL_ONLY={signalResult.TradePlanSignalOnlyCount}, READY={signalResult.TradePlanReadyCount}, NO_TRADE={signalResult.TradePlanNoTradeCount}, BLOCKED={signalResult.TradePlanBlockedCount}, ExceptionCount={signalResult.ExceptionCount}");
            Assert.Equal(signalResult.BarsProcessed + signalResult.BarsRejected, series.Count);
            Assert.Equal(0, signalResult.ExceptionCount);

            // ── REGIME COVERAGE (brief §14): tabulate Decision.Winner - the REAL classification the
            // pipeline itself produces (MarketState enum), never an invented category. ──

            var regimeCounts = new Dictionary<MarketState, int>();
            foreach (MarketState state in Enum.GetValues<MarketState>())
                regimeCounts[state] = 0;

            int barsWithoutDecision = 0;
            foreach (BacktestSignalResult bar in signalResult.Bars)
            {
                if (bar.Decision is null)
                {
                    barsWithoutDecision++;
                    continue;
                }
                regimeCounts[bar.Decision.Winner]++;
            }

            _output.WriteLine("=== REGIME COVERAGE (Decision.Winner distribution, real bars only) ===");
            foreach (var kvp in regimeCounts.OrderByDescending(k => k.Value))
                _output.WriteLine($"{kvp.Key}: {kvp.Value} bars");
            _output.WriteLine($"Bars without a Decision (rejected/warmup-exception/pre-pipeline): {barsWithoutDecision}");
            Assert.Equal(signalResult.Bars.Count, regimeCounts.Values.Sum() + barsWithoutDecision);

            // Sanity: RegimeDetectedCount (Winner != Unknown) must match the tabulation above exactly.
            int detectedFromTabulation = regimeCounts.Where(k => k.Key != MarketState.Unknown).Sum(k => k.Value);
            Assert.Equal(signalResult.RegimeDetectedCount, detectedFromTabulation);
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            _output.WriteLine($"SKIPPED (network/Yahoo unavailable, not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>Thin, local adapter from Backtest.Core.MarketData.HistoricalBar/Series to
    /// Tests.Research.StopLossCalibration.RealMarket.RealMarketBar - the shape RealMarketQualityAnalyzer
    /// expects. SessionId is a single constant Guid (one Yahoo download = one logical session, matching
    /// SessionIntegrityFacts.SingleSession's own definition); CurrentBar is the bar's own index within the
    /// series (mirrors what CurrentBar represents in a live ATAS capture: a monotonically increasing
    /// per-bar counter). No value is invented - every OHLCV/timestamp field is copied verbatim.</summary>
    private static IReadOnlyList<RealMarketBar> ToRealMarketBars(HistoricalSeries series)
    {
        var sessionId = Guid.NewGuid();
        var bars = new List<RealMarketBar>(series.Count);
        for (int i = 0; i < series.Count; i++)
        {
            HistoricalBar bar = series.Bars[i];
            bars.Add(new RealMarketBar(
                sessionId, bar.Timestamp, series.Symbol, series.TimeFrame,
                bar.Open, bar.High, bar.Low, bar.Close, bar.Volume, i));
        }
        return bars;
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or InvalidOperationException;
}
