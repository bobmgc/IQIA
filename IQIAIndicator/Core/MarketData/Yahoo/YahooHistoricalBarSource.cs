using System;
using System.Collections.Generic;

namespace IQIAIndicator.Core.MarketData.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 14.2). <see cref="IHistoricalBarSource"/> backed by Yahoo Finance's chart endpoint.
/// Yahoo → <see cref="YahooChartParser"/> → <see cref="HistoricalSeries"/> is the entire contract: no
/// class outside <c>Core/MarketData/Yahoo</c> ever sees a Yahoo ticker, a Yahoo interval string, or the
/// raw chart JSON - the Backtest Engine, RegimeEngine and every other pipeline component consume the same
/// provider-agnostic <see cref="HistoricalSeries"/> that <see cref="CsvHistoricalBarSource"/> produces
/// (Lot 14.1). Never referenced by RiskEngine, DecisionArbitrator, EntryTriggerBuilder, TradePlanBuilder,
/// any ATAS adapter, or any dashboard - Yahoo is exclusively a historical DATA source (brief §1).
///
/// TIMEZONE (brief §11): every <see cref="HistoricalBar.Timestamp"/> this class produces is normalized to
/// UTC directly from Yahoo's own Unix-epoch-second <c>timestamp</c> array (see
/// <see cref="YahooChartParser"/>) - Unix time is UTC by definition, so this conversion is a pure,
/// deterministic calculation with no dependency on <c>DateTime.Now</c>/<c>DateTime.UtcNow</c> or on the
/// host machine's local time zone. The resulting <see cref="HistoricalSeries.TimeZone"/> is always the
/// literal string "UTC". This is a genuine improvement over the live ATAS path, which passes
/// <c>IndicatorCandle.Time</c> through with <c>DateTimeKind.Unspecified</c> and no conversion at all (Lot
/// 13 report §3.2b) - a difference to account for before any future cross-source timestamp comparison.
///
/// ADJUSTED CLOSE (brief §7): Yahoo's chart endpoint returns an <c>indicators.adjclose</c> series only for
/// certain instrument types/intervals (typically daily equities with dividends/splits) - verified
/// empirically absent for MES=F/ES=F at 5-minute granularity (futures have no dividend/split adjustment
/// concept to begin with). This class never reads <c>adjclose</c> under any circumstance: only
/// <c>indicators.quote[0].close</c> (the raw traded close) ever becomes <see cref="HistoricalBar.Close"/>.
/// For a hypothetical future non-futures symbol where Yahoo DOES return adjclose, this remains true - the
/// raw Close is used, Adjusted Close is never substituted, silently or otherwise.
///
/// CONTINUOUS FUTURES (brief §14/§15): the only two symbols this lot resolves (<see cref="YahooSymbolMap"/>
/// - "ES"/"MES") map to Yahoo's CONTINUOUS front-month tickers ("ES=F"/"MES=F"), which Yahoo itself splices
/// across contract rolls - never an individual expiring contract. Every <see cref="HistoricalSeries"/>
/// this class returns carries <c>Provider = "Yahoo(Continuous)"</c> specifically so this caveat travels
/// with the data itself, not just this doc comment - a consumer inspecting <c>series.Provider</c> sees it
/// without having to already know to look here. YAHOO FUTURES SYMBOL SUPPORT = LIMITED: exactly ES and
/// MES, both continuous, both futures - nothing else is claimed.
///
/// GAPS AND DEPTH LIMITS (brief §9/§12): <see cref="LastRequestGapCount"/> and
/// <see cref="LastRequestChunkCount"/> report, after each <see cref="Load"/> call, how many Yahoo-reported
/// gap slots were omitted and how many chunk requests were issued - observational counters in the same
/// spirit as <c>ScientificDatasetCollector</c>'s own <c>RecordsAccepted</c>/<c>DuplicateRecordsRejected</c>
/// (never influencing the result, only reporting on it), because <see cref="IHistoricalBarSource"/>'s
/// return type has no room for auxiliary diagnostics and this lot does not modify that interface.
/// </summary>
public sealed class YahooHistoricalBarSource : IHistoricalBarSource
{
    /// <summary>Yahoo's own documented (and empirically confirmed 2026-08-22) limit for 5-minute data is
    /// 60 days; 59 leaves one day of safety margin rather than re-deriving the exact boundary.</summary>
    public const int DefaultMaxChunkSpanDays = 59;

    private readonly IYahooChartClient _client;
    private readonly int _maxChunkSpanDays;

    public YahooHistoricalBarSource() : this(new HttpYahooChartClient(), DefaultMaxChunkSpanDays)
    {
    }

    internal YahooHistoricalBarSource(IYahooChartClient client, int maxChunkSpanDays = DefaultMaxChunkSpanDays)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (maxChunkSpanDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxChunkSpanDays), maxChunkSpanDays, "maxChunkSpanDays must be positive.");

        _client = client;
        _maxChunkSpanDays = maxChunkSpanDays;
    }

    /// <summary>Gap slots (Yahoo-reported, OHLCV-null timestamps) omitted by the most recent
    /// <see cref="Load"/> call - 0 before any call has been made. See the class doc comment.</summary>
    public int LastRequestGapCount { get; private set; }

    /// <summary>Number of chunked HTTP requests the most recent <see cref="Load"/> call issued - 0 before
    /// any call has been made. Always 1 for a request within <see cref="DefaultMaxChunkSpanDays"/>.</summary>
    public int LastRequestChunkCount { get; private set; }

    public HistoricalSeries Load(string symbol, string timeFrame, DateTime from, DateTime to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeFrame);

        if (to <= from)
            throw new ArgumentException($"Requested range is empty or inverted: from={from:O}, to={to:O} (to is exclusive).", nameof(to));

        string yahooTicker = YahooSymbolMap.Resolve(symbol);
        string yahooInterval = YahooTimeFrameMap.ResolveYahooInterval(timeFrame);

        IReadOnlyList<(DateTime From, DateTime To)> chunks = YahooChunkPlanner.Plan(from, to, _maxChunkSpanDays);

        var allBars = new List<HistoricalBar>();
        int totalGapCount = 0;

        for (int i = 0; i < chunks.Count; i++)
        {
            // Sprint 15.25 (Lot 14.2, brief §10). Chunks are logically back-to-back
            // (chunk[i].To == chunk[i+1].From). Querying Yahoo with that exact shared instant on BOTH
            // sides risks Yahoo returning the same boundary bar twice (its period1/period2 bounds are not
            // documented as strictly exclusive). Every chunk after the first therefore queries starting
            // one second later than its logical boundary - the two HTTP requests can then never both
            // contain the same Unix-second grid point, so no duplicate can arise from chunking itself.
            // HistoricalSeries.Create's existing, unmodified duplicate/out-of-order detection remains the
            // final safety net regardless (brief §10: "Sinon : REJECT") - this is defence in depth, not a
            // replacement for it.
            DateTime queryFrom = i == 0 ? chunks[i].From : chunks[i].From.AddSeconds(1);
            DateTime queryTo = chunks[i].To;

            string json = _client.FetchChartJson(yahooTicker, yahooInterval, queryFrom, queryTo);
            YahooChartParser.ParseResult parsed = YahooChartParser.Parse(json);

            allBars.AddRange(parsed.Bars);
            totalGapCount += parsed.GapCount;
        }

        LastRequestGapCount = totalGapCount;
        LastRequestChunkCount = chunks.Count;

        if (allBars.Count == 0)
        {
            throw new InvalidOperationException(
                $"Yahoo returned no usable bar for {symbol} ({yahooTicker}) {timeFrame} in [{from:O}, {to:O}) " +
                $"across {chunks.Count} chunk(s) - {totalGapCount} gap slot(s) were reported by Yahoo and omitted, " +
                "never fabricated. This may mean the range contains no trading activity at all.");
        }

        return HistoricalSeries.Create(symbol, timeFrame, "UTC", "Yahoo(Continuous)", allBars);
    }
}
