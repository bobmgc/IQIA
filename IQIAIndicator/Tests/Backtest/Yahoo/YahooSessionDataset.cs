using System;
using System.Collections.Generic;
using System.Threading;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using Xunit;

[assembly: AssemblyFixture(typeof(IQIAIndicator.Tests.BacktestTests.Yahoo.YahooSessionDataset))]

namespace IQIAIndicator.Tests.BacktestTests.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 15.25-XX, brief Phase 7 - REQUEST MINIMIZATION). One real acquisition of the
/// canonical <c>MES / M5 / DefaultMaxChunkSpanDays</c> Yahoo window per test run, shared by every test
/// that takes it as a constructor parameter.
///
/// Before this: ~20 integration tests each issued their own identical 59-day MES/M5 download during a
/// full run - dozens of the same request, the direct cause of the HTTP 429 that then hung the suite.
/// After this: those tests share ONE download and consume the resulting <see cref="HistoricalSeries"/>,
/// which is already deeply immutable (built once by <c>HistoricalSeries.Create</c>, never mutated) - so
/// RUN ISOLATION and DETERMINISM are preserved: every consumer sees byte-identical bars, and none can
/// perturb another.
///
/// Sprint 15.25 (Lot 16.6): this is now an xUnit v3 <c>[assembly: AssemblyFixture(...)]</c> - a single
/// instance for the WHOLE assembly. Under xUnit 2.5.3 the only way to share one instance across test
/// classes was <c>ICollectionFixture</c>, which also force-serialised the collection (26 heavy backtest
/// classes running one after another). An assembly fixture shares the instance WITHOUT that
/// serialisation, so the consuming classes are free to run in parallel again while still downloading
/// exactly once.
///
/// The acquisition uses the SAME bounded, classified <see cref="YahooHistoricalBarSource"/> as
/// production. If Yahoo is unavailable, <see cref="Series"/> is <c>null</c> and <see cref="Failure"/> /
/// <see cref="SkipReason"/> are populated - a consuming test calls <see cref="Require"/> to skip
/// (<see cref="Xunit.Assert.Skip(string)"/>) with an explicit "provider unavailable" reason.
/// </summary>
public sealed class YahooSessionDataset
{
    public YahooSessionDataset()
    {
        try
        {
            var source = new YahooHistoricalBarSource();
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-YahooHistoricalBarSource.DefaultMaxChunkSpanDays);

            // Bounded by YahooRetryPolicy + the overall-load ceiling: this constructor cannot hang.
            Series = source.Load("MES", "M5", from, to, CancellationToken.None);
            RequestedFromUtc = from;
            RequestedToUtc = to;
            GapCount = source.LastRequestGapCount;
            ChunkCount = source.LastRequestChunkCount;
        }
        catch (YahooProviderException ex)
        {
            Failure = ex;
            SkipReason = $"SKIPPED (Yahoo provider unavailable, not a code failure): {ex.Kind} " +
                $"(HTTP {ex.HttpStatusCode?.ToString() ?? "n/a"}, {ex.AttemptsMade} attempt(s), " +
                $"{ex.TotalWaited.TotalSeconds:0.#}s backoff) - {ex.Message}";
        }
    }

    /// <summary>The shared, immutable series - or <c>null</c> when Yahoo was unavailable this run.</summary>
    public HistoricalSeries? Series { get; }

    /// <summary>The classified provider failure, when <see cref="Series"/> is <c>null</c>.</summary>
    public YahooProviderException? Failure { get; }

    /// <summary>Ready-to-log explanation when <see cref="Series"/> is <c>null</c>.</summary>
    public string? SkipReason { get; }

    public DateTime RequestedFromUtc { get; }
    public DateTime RequestedToUtc { get; }
    public int GapCount { get; }
    public int ChunkCount { get; }

    /// <summary>Sprint 15.25 (Lot 16.6). Returns the shared series, or - when Yahoo was unavailable this
    /// run - marks the calling test <b>Skipped</b> via xUnit v3's <see cref="Xunit.Assert.SkipUnless"/>
    /// with an explicit provider-unavailable reason (replacing the pre-v3 "log a SKIPPED line and
    /// early-return from a still-Passed test" convention).</summary>
    public HistoricalSeries Require()
    {
        Assert.SkipUnless(Series is not null, SkipReason ?? "Yahoo provider unavailable (not a code failure).");
        return Series!;
    }

    /// <summary>
    /// Sprint 15.25 (Lot 16.5). The shared session series sliced to its last <paramref name="days"/>
    /// calendar days - the same rolling horizon the pre-16.5 <c>*YahooIntegrationTests</c> requested with
    /// <c>DateTime.UtcNow.AddDays(-45)</c>, minus the redundant download. The slice is a contiguous
    /// suffix of an already-validated series, so it re-validates trivially through the same unmodified
    /// <see cref="HistoricalSeries.Create"/>. Skips the calling test (Lot 16.6) when Yahoo was
    /// unavailable this run, exactly like <see cref="Require"/>.
    /// </summary>
    public HistoricalSeries LastDays(int days)
    {
        if (days <= 0)
            throw new ArgumentOutOfRangeException(nameof(days), days, "days must be positive.");

        HistoricalSeries full = Require();

        DateTime cutoff = full.LastTimestamp.AddDays(-days);
        var slice = new List<HistoricalBar>();
        foreach (HistoricalBar bar in full.Bars)
        {
            if (bar.Timestamp >= cutoff)
                slice.Add(bar);
        }

        return HistoricalSeries.Create(full.Symbol, full.TimeFrame, full.TimeZone, full.Provider, slice);
    }
}
