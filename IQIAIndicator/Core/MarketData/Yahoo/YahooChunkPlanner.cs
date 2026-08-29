using System;
using System.Collections.Generic;

namespace IQIAIndicator.Core.MarketData.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 14.2, brief §10). Pure, network-free split of a [from, to) request range into
/// consecutive half-open sub-ranges no larger than <c>maxSpanDays</c>, honouring Yahoo's own documented
/// (and empirically confirmed, 2026-08-22) limit for 5-minute data: a live request for a &gt;60-day-old
/// window returned HTTP 422 with "5m data not available for startTime=... The requested range must be
/// within the last 60 days." <see cref="YahooHistoricalBarSource"/> uses 59 as its default
/// <c>maxSpanDays</c> - one day of safety margin under Yahoo's own boundary, not a re-derivation of it.
///
/// Chunks are LOGICALLY half-open and back-to-back (chunk[i].To == chunk[i+1].From) - see
/// <see cref="YahooHistoricalBarSource"/> for how the +1-second adjustment that keeps the actual HTTP
/// queries from ever requesting the same instant twice is applied; this class only computes the logical
/// boundaries, so its own correctness (every requested second covered exactly once, in order) is
/// independently testable without any HTTP concern mixed in.
/// </summary>
internal static class YahooChunkPlanner
{
    public static IReadOnlyList<(DateTime From, DateTime To)> Plan(DateTime from, DateTime to, int maxSpanDays)
    {
        if (to <= from)
            throw new ArgumentException($"Requested range is empty or inverted: from={from:O}, to={to:O} (to is exclusive).", nameof(to));

        if (maxSpanDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSpanDays), maxSpanDays, "maxSpanDays must be positive.");

        var maxSpan = TimeSpan.FromDays(maxSpanDays);
        var chunks = new List<(DateTime, DateTime)>();
        DateTime cursor = from;

        while (cursor < to)
        {
            DateTime chunkEnd = cursor + maxSpan;
            if (chunkEnd > to)
                chunkEnd = to;

            chunks.Add((cursor, chunkEnd));
            cursor = chunkEnd;
        }

        return chunks;
    }
}
