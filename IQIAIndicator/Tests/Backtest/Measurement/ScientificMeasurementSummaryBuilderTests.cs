using System;
using System.Collections.Generic;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Engine.EntryTrigger;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Measurement;

/// <summary>
/// Sprint 15.25 (Lot 14.4, brief §32/§33/§34). <see cref="ScientificMeasurementSummaryBuilder"/> - median
/// convention, ALL/BUY/SELL separation, hit-rate aggregation, and exclusion of anything that isn't
/// <see cref="MeasurementStatus.Measured"/> from the statistics.
/// </summary>
public sealed class ScientificMeasurementSummaryBuilderTests
{
    private static readonly DateTime Anchor = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    private static MeasurementResult Measured(
        int index, DirectionCandidate direction, double ret, double mfe, double mae, params bool[] hits)
    {
        var hitResults = new List<MeasurementHitResult>();
        for (int i = 0; i < hits.Length; i++)
            hitResults.Add(new MeasurementHitResult(0.001 * (i + 1), hits[i]));

        return new MeasurementResult(
            index, Anchor.AddMinutes(index), direction, 100m, MeasurementStatus.Measured, null, 10,
            ret, mfe, mae, hitResults.AsReadOnly());
    }

    private static MeasurementResult NotMeasured(int index, MeasurementStatus status) =>
        new(index, Anchor.AddMinutes(index), DirectionCandidate.NO_ACTION, null, status, "excluded from aggregation", 10,
            null, null, null, Array.Empty<MeasurementHitResult>());

    // ── §33: median convention ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Median_OddCount_ReturnsTheMiddleElement()
    {
        Assert.Equal(2.0, ScientificMeasurementSummaryBuilder.Median(new List<double> { 3.0, 1.0, 2.0 }));
    }

    [Fact]
    public void Median_EvenCount_ReturnsTheAverageOfTheTwoMiddleElements()
    {
        Assert.Equal(2.5, ScientificMeasurementSummaryBuilder.Median(new List<double> { 1.0, 2.0, 3.0, 4.0 }));
    }

    [Fact]
    public void Median_SingleValue_ReturnsThatValue()
    {
        Assert.Equal(7.0, ScientificMeasurementSummaryBuilder.Median(new List<double> { 7.0 }));
    }

    [Fact]
    public void Median_EmptyInput_ReturnsNull_NeverZero()
    {
        Assert.Null(ScientificMeasurementSummaryBuilder.Median(new List<double>()));
    }

    [Fact]
    public void Median_IsOrderIndependent()
    {
        double? a = ScientificMeasurementSummaryBuilder.Median(new List<double> { 5.0, -1.0, 3.0, 0.0, 2.0 });
        double? b = ScientificMeasurementSummaryBuilder.Median(new List<double> { -1.0, 0.0, 2.0, 3.0, 5.0 });
        Assert.Equal(a, b);
        Assert.Equal(2.0, a);
    }

    // ── §32/§34: ALL/BUY/SELL separation, count, median, hit rate ───────────────────────────────────

    [Fact]
    public void Summarize_SeparatesBuyAndSellStatistics_WithoutLosingThemInTheAllTotal()
    {
        var measurements = new List<MeasurementResult>
        {
            Measured(0, DirectionCandidate.BUY_CANDIDATE, ret: 0.02, mfe: 0.03, mae: 0.01, hits: new[] { true, false }),
            Measured(1, DirectionCandidate.BUY_CANDIDATE, ret: -0.01, mfe: 0.01, mae: 0.02, hits: new[] { true, false }),
            Measured(2, DirectionCandidate.SELL_CANDIDATE, ret: 0.05, mfe: 0.05, mae: 0.00, hits: new[] { true, true }),
        };

        ScientificMeasurementSummaryByDirection summary = ScientificMeasurementSummaryBuilder.Summarize(measurements);

        Assert.Equal(3, summary.All.Count);
        Assert.Equal(2, summary.Buy.Count);
        Assert.Equal(1, summary.Sell.Count);

        // BUY median return over {0.02, -0.01} = average = 0.005
        Assert.Equal(0.005, summary.Buy.MedianReturn!.Value, 9);
        // SELL median return over {0.05} = 0.05
        Assert.Equal(0.05, summary.Sell.MedianReturn!.Value, 9);
        // ALL median return over {0.02, -0.01, 0.05} sorted {-0.01, 0.02, 0.05} -> middle = 0.02
        Assert.Equal(0.02, summary.All.MedianReturn!.Value, 9);
    }

    [Fact]
    public void Summarize_HitRate_IsAFractionOfMeasuredSignals_NeverAPercentage()
    {
        var measurements = new List<MeasurementResult>
        {
            Measured(0, DirectionCandidate.BUY_CANDIDATE, 0.01, 0.01, 0.01, true, false),
            Measured(1, DirectionCandidate.BUY_CANDIDATE, 0.01, 0.01, 0.01, true, true),
            Measured(2, DirectionCandidate.BUY_CANDIDATE, 0.01, 0.01, 0.01, false, false),
            Measured(3, DirectionCandidate.BUY_CANDIDATE, 0.01, 0.01, 0.01, false, false),
        };

        ScientificMeasurementSummaryByDirection summary = ScientificMeasurementSummaryBuilder.Summarize(measurements);

        // Threshold[0] (+0.10%): 2 hits / 4 measured = 0.5
        Assert.Equal(0.5, summary.All.HitRates[0].HitRate, 9);
        Assert.Equal(2, summary.All.HitRates[0].HitCount);
        Assert.Equal(4, summary.All.HitRates[0].MeasuredCount);

        // Threshold[1] (+0.20%): 1 hit / 4 measured = 0.25
        Assert.Equal(0.25, summary.All.HitRates[1].HitRate, 9);
    }

    [Fact]
    public void Summarize_ExcludesEveryNonMeasuredStatus_FromCountAndMedian()
    {
        var measurements = new List<MeasurementResult>
        {
            Measured(0, DirectionCandidate.BUY_CANDIDATE, 0.02, 0.02, 0.0, true),
            NotMeasured(1, MeasurementStatus.NotMeasurable),
            NotMeasured(2, MeasurementStatus.InvalidEntryPrice),
            NotMeasured(3, MeasurementStatus.InsufficientFutureData),
            NotMeasured(4, MeasurementStatus.InvalidFutureData),
        };

        ScientificMeasurementSummaryByDirection summary = ScientificMeasurementSummaryBuilder.Summarize(measurements);

        Assert.Equal(1, summary.All.Count);
        Assert.Equal(0.02, summary.All.MedianReturn!.Value, 9);
    }

    [Fact]
    public void Summarize_EmptyInput_ProducesZeroCountAndNullMedians_NeverThrows()
    {
        ScientificMeasurementSummaryByDirection summary = ScientificMeasurementSummaryBuilder.Summarize(new List<MeasurementResult>());

        Assert.Equal(0, summary.All.Count);
        Assert.Null(summary.All.MedianReturn);
        Assert.Null(summary.All.MedianMfe);
        Assert.Null(summary.All.MedianMae);
        Assert.Empty(summary.All.HitRates);
    }

    [Fact]
    public void Summarize_OnlyBuySignals_ProducesAnEmptySellSummary()
    {
        var measurements = new List<MeasurementResult>
        {
            Measured(0, DirectionCandidate.BUY_CANDIDATE, 0.01, 0.01, 0.0, true),
            Measured(1, DirectionCandidate.BUY_CANDIDATE, 0.02, 0.02, 0.0, true),
        };

        ScientificMeasurementSummaryByDirection summary = ScientificMeasurementSummaryBuilder.Summarize(measurements);

        Assert.Equal(2, summary.All.Count);
        Assert.Equal(2, summary.Buy.Count);
        Assert.Equal(0, summary.Sell.Count);
        Assert.Null(summary.Sell.MedianReturn);
    }
}
