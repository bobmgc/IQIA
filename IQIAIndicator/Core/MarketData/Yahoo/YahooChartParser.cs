using System;
using System.Collections.Generic;
using System.Text.Json;

namespace IQIAIndicator.Core.MarketData.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 14.2, brief §6/§12). Pure JSON -&gt; <see cref="HistoricalBar"/> mapping - no HTTP,
/// no network, no ATAS. Deterministic and independently unit-testable against literal fixture strings
/// (Lot 14.2 brief §5/§19), separate from <see cref="YahooHistoricalBarSource"/>'s HTTP orchestration.
///
/// Mapping (brief §6): timestamp -&gt; Timestamp (Unix seconds -&gt; UTC DateTime, never local/Now/UtcNow),
/// open/high/low/close/volume -&gt; the identically-named HistoricalBar fields, verbatim. BidVolume/
/// AskVolume/Delta/OpenInterest are NEVER populated - Yahoo's chart endpoint has no equivalent fields
/// (verified against the real response shape), so they stay null on every HistoricalBar this parser
/// produces, exactly like <see cref="HistoricalBar"/>'s own "never fabricated" contract requires.
///
/// GAP HANDLING (brief §6/§12, empirically grounded): a real capture of MES=F at 5m granularity showed
/// 84 of 287 timestamp slots with Open/High/Low/Close all null simultaneously (Volume=0) during a CME
/// daily maintenance break - Yahoo still emits the timestamp on its fixed 5-minute grid but reports no
/// bar. This is NOT malformed data to reject and NOT a value to interpolate/forward-fill/fabricate: it is
/// Yahoo's own signal that no bar was formed at that slot. Any index where Open/High/Low/Close/Volume is
/// null (individually or together) is OMITTED from the result - never zero-filled, never carried forward
/// from the previous bar - and counted in <see cref="ParseResult.GapCount"/> so the caller can report it
/// (brief §9: "NE PAS masquer la perte de données") rather than have it disappear silently.
/// </summary>
internal static class YahooChartParser
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public sealed record ParseResult(IReadOnlyList<HistoricalBar> Bars, int GapCount);

    /// <exception cref="InvalidOperationException">The JSON is not the expected Yahoo chart envelope
    /// shape, or Yahoo's own <c>chart.error</c> field is populated (its Code/Description are included
    /// verbatim in the exception message - never swallowed, never translated into a guess).</exception>
    public static ParseResult Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        YahooChartEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<YahooChartEnvelope>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Yahoo chart response is not valid JSON: {exception.Message}", exception);
        }

        YahooChartRoot? chart = envelope?.Chart;
        if (chart is null)
            throw new InvalidOperationException("Yahoo chart response did not contain a \"chart\" envelope.");

        if (chart.Error is { } error)
        {
            throw new InvalidOperationException(
                $"Yahoo chart API returned an error: {error.Code ?? "(no code)"} - {error.Description ?? "(no description)"}");
        }

        if (chart.Result is null || chart.Result.Length == 0)
            throw new InvalidOperationException("Yahoo chart response had no error but also no result set - unexpected shape.");

        YahooChartResult result = chart.Result[0];

        // Shape 2 (class doc comment): a genuinely empty period (e.g. a full weekend day) has no
        // "timestamp" key at all - never an error, never a defect, just nothing to report.
        if (result.Timestamp is null || result.Timestamp.Length == 0)
            return new ParseResult(Array.Empty<HistoricalBar>(), 0);

        YahooChartQuote? quote = result.Indicators?.Quote is { Length: > 0 } quotes ? quotes[0] : null;
        int n = result.Timestamp.Length;

        if (quote is null || !SameLength(quote, n))
        {
            throw new InvalidOperationException(
                $"Yahoo chart response has {n} timestamp(s) but its quote arrays are missing or a different length - unexpected shape.");
        }

        var bars = new List<HistoricalBar>(n);
        int gapCount = 0;

        for (int i = 0; i < n; i++)
        {
            decimal? open = quote.Open![i];
            decimal? high = quote.High![i];
            decimal? low = quote.Low![i];
            decimal? close = quote.Close![i];
            decimal? volume = quote.Volume![i];

            if (open is null || high is null || low is null || close is null || volume is null)
            {
                gapCount++;
                continue;
            }

            DateTime timestampUtc = DateTimeOffset.FromUnixTimeSeconds(result.Timestamp[i]).UtcDateTime;
            bars.Add(new HistoricalBar(timestampUtc, open.Value, high.Value, low.Value, close.Value, volume.Value));
        }

        return new ParseResult(bars, gapCount);
    }

    private static bool SameLength(YahooChartQuote quote, int expected) =>
        quote.Open?.Length == expected
        && quote.High?.Length == expected
        && quote.Low?.Length == expected
        && quote.Close?.Length == expected
        && quote.Volume?.Length == expected;
}
