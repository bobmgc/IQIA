using System;
using System.Linq;
using IQIAIndicator.Core.MarketData.Yahoo;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Yahoo;

/// <summary>Sprint 15.25 (Lot 14.2, brief §10). Pure chunk-boundary planning, independent of any HTTP
/// concern - every requested second must be covered exactly once, in chronological order.</summary>
public sealed class YahooChunkPlannerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SpanWithinLimit_ProducesExactlyOneChunk()
    {
        var chunks = YahooChunkPlanner.Plan(T0, T0.AddDays(10), maxSpanDays: 59);

        Assert.Single(chunks);
        Assert.Equal(T0, chunks[0].From);
        Assert.Equal(T0.AddDays(10), chunks[0].To);
    }

    [Fact]
    public void SpanExactlyAtLimit_ProducesExactlyOneChunk()
    {
        var chunks = YahooChunkPlanner.Plan(T0, T0.AddDays(59), maxSpanDays: 59);

        Assert.Single(chunks);
    }

    [Fact]
    public void SpanOverLimit_ProducesMultipleChunks_EachAtMostMaxSpan()
    {
        var chunks = YahooChunkPlanner.Plan(T0, T0.AddDays(130), maxSpanDays: 59);

        Assert.Equal(3, chunks.Count); // 59 + 59 + 12
        foreach (var chunk in chunks)
            Assert.True((chunk.To - chunk.From) <= TimeSpan.FromDays(59));
    }

    [Fact]
    public void Chunks_AreLogicallyBackToBack_NoGapNoOverlap()
    {
        var chunks = YahooChunkPlanner.Plan(T0, T0.AddDays(130), maxSpanDays: 59);

        for (int i = 1; i < chunks.Count; i++)
            Assert.Equal(chunks[i - 1].To, chunks[i].From);
    }

    [Fact]
    public void Chunks_CoverTheFullRequestedRange_FirstFromAndLastTo()
    {
        var chunks = YahooChunkPlanner.Plan(T0, T0.AddDays(130), maxSpanDays: 59);

        Assert.Equal(T0, chunks[0].From);
        Assert.Equal(T0.AddDays(130), chunks[^1].To);
    }

    [Fact]
    public void Chunks_AreChronologicallyOrdered()
    {
        var chunks = YahooChunkPlanner.Plan(T0, T0.AddDays(200), maxSpanDays: 59);

        Assert.Equal(chunks.OrderBy(c => c.From), chunks);
    }

    [Fact]
    public void InvertedRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => YahooChunkPlanner.Plan(T0, T0, maxSpanDays: 59));
        Assert.Throws<ArgumentException>(() => YahooChunkPlanner.Plan(T0.AddDays(1), T0, maxSpanDays: 59));
    }

    [Fact]
    public void NonPositiveMaxSpanDays_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => YahooChunkPlanner.Plan(T0, T0.AddDays(1), maxSpanDays: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => YahooChunkPlanner.Plan(T0, T0.AddDays(1), maxSpanDays: -5));
    }
}
