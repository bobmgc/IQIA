namespace IQIAIndicator.Core.MarketData.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 14.2). Plain DTOs mirroring Yahoo Finance's undocumented "chart" JSON envelope
/// (<c>https://query2.finance.yahoo.com/v8/finance/chart/{ticker}</c>), verified empirically against the
/// live endpoint on 2026-08-22 (real HTTP calls, not inferred from documentation - Yahoo publishes none
/// for this endpoint). Deserialized with <c>JsonSerializerOptions.PropertyNameCaseInsensitive = true</c>
/// (see <see cref="YahooChartParser"/>), so no <c>[JsonPropertyName]</c> attributes are needed - Yahoo's
/// camelCase keys (e.g. "exchangeTimezoneName", "gmtoffset") match these PascalCase properties
/// case-insensitively.
///
/// Every array-shaped field is nullable because THREE distinct, real response shapes were observed and
/// must all be represented:
///   1. Success with bars: <see cref="YahooChartResult.Timestamp"/> and every
///      <see cref="YahooChartQuote"/> array are populated (same length as Timestamp).
///   2. Success, no bars in range (e.g. a full non-trading day): <see cref="YahooChartResult.Timestamp"/>
///      is entirely ABSENT (not an empty array - the JSON key itself is missing), and
///      <see cref="YahooChartResult.Indicators"/>.Quote[0] is an empty object with no OHLCV arrays at all.
///   3. Error (unsupported range, unknown symbol, etc.): <see cref="YahooChartRoot.Result"/> is null and
///      <see cref="YahooChartRoot.Error"/> carries a structured Code/Description - e.g. observed verbatim:
///      "5m data not available for startTime=... and endTime=.... The requested range must be within the
///      last 60 days." and "No data found, symbol may be delisted".
/// Within a populated quote, individual OHLC entries can ALSO be null at specific indices (observed: 84 of
/// 287 five-minute slots null during a CME daily maintenance break) - Yahoo still emits the timestamp slot
/// on its fixed grid but reports no bar. <see cref="YahooChartParser"/> treats this as a gap, never as a
/// fabricated zero/interpolated value.
/// </summary>
internal sealed class YahooChartEnvelope
{
    public YahooChartRoot? Chart { get; set; }
}

internal sealed class YahooChartRoot
{
    public YahooChartResult[]? Result { get; set; }

    public YahooChartError? Error { get; set; }
}

internal sealed class YahooChartError
{
    public string? Code { get; set; }

    public string? Description { get; set; }
}

internal sealed class YahooChartResult
{
    public YahooChartMeta? Meta { get; set; }

    /// <summary>Unix epoch seconds (UTC by definition - Unix time carries no timezone ambiguity).
    /// Absent entirely (not empty) when the requested range contains no trading activity at all.</summary>
    public long[]? Timestamp { get; set; }

    public YahooChartIndicators? Indicators { get; set; }
}

internal sealed class YahooChartMeta
{
    public string? Symbol { get; set; }

    /// <summary>Abbreviated, DST-dependent zone name (e.g. "EDT") - NOT used for any conversion here;
    /// see <see cref="ExchangeTimezoneName"/> and <see cref="YahooChartParser"/>'s doc comment.</summary>
    public string? Timezone { get; set; }

    /// <summary>IANA time zone database name (e.g. "America/New_York") - informational only in this lot;
    /// every timestamp is normalized from the UTC Unix epoch directly, never derived from this field.</summary>
    public string? ExchangeTimezoneName { get; set; }

    public int? Gmtoffset { get; set; }

    public string? InstrumentType { get; set; }

    public string? DataGranularity { get; set; }
}

internal sealed class YahooChartIndicators
{
    public YahooChartQuote[]? Quote { get; set; }
}

internal sealed class YahooChartQuote
{
    public decimal?[]? Open { get; set; }

    public decimal?[]? High { get; set; }

    public decimal?[]? Low { get; set; }

    public decimal?[]? Close { get; set; }

    public decimal?[]? Volume { get; set; }
}
