using System;
using System.Collections.Generic;
using System.Globalization;

namespace IQIAIndicator.Core.MarketData;

/// <summary>
/// Sprint 15.25 (Lot 14.1, brief §4). A chronologically ordered, fully validated sequence of
/// <see cref="HistoricalBar"/> for exactly one (Symbol, TimeFrame) pair, from exactly one provider.
///
/// THE CENTRAL RULE OF THIS TYPE: it DETECTS bad data, it never REPAIRS it. No silent sort, no
/// de-duplication, no gap filling, no clamping. Every construction path either returns a series whose
/// invariants all hold, or reports precisely which ones do not. This mirrors the discipline the
/// pre-existing Tests-side <c>RealMarketOhlcvCsvReader</c> already applies ("never fabricates a row it
/// cannot parse - a malformed row throws rather than being silently skipped or defaulted").
///
/// <see cref="TimeZone"/> is REQUIRED and has no default, on purpose. The Sprint 15.16 audit established
/// that no timezone handling exists anywhere in this codebase - the live path passes ATAS's
/// <c>IndicatorCandle.Time</c> through unconverted, and the research harness stamps
/// <c>DateTime.UtcNow</c>. A default here would silently reproduce that ambiguity on real data where it
/// has consequences; forcing the caller to name the convention makes it auditable instead.
/// This type performs NO timezone conversion - it only records what the caller declared.
/// </summary>
public sealed class HistoricalSeries
{
    private HistoricalSeries(
        string symbol,
        string timeFrame,
        string timeZone,
        string provider,
        IReadOnlyList<HistoricalBar> bars)
    {
        Symbol = symbol;
        TimeFrame = timeFrame;
        TimeZone = timeZone;
        Provider = provider;
        Bars = bars;
    }

    public string Symbol { get; }

    public string TimeFrame { get; }

    /// <summary>The timezone/session convention the CALLER declares these timestamps follow. Recorded
    /// verbatim, never interpreted or converted by this type.</summary>
    public string TimeZone { get; }

    public string Provider { get; }

    public IReadOnlyList<HistoricalBar> Bars { get; }

    public int Count => Bars.Count;

    public DateTime FirstTimestamp => Bars[0].Timestamp;

    public DateTime LastTimestamp => Bars[Bars.Count - 1].Timestamp;

    /// <summary>
    /// Builds a validated series, or throws <see cref="ArgumentException"/> naming every violation.
    /// Use this whenever a failure should abort the caller (loading a scenario, an adapter reading a
    /// file); use <see cref="TryCreate"/> when the caller wants to inspect the violations itself.
    /// </summary>
    public static HistoricalSeries Create(
        string symbol,
        string timeFrame,
        string timeZone,
        string provider,
        IReadOnlyList<HistoricalBar> bars)
    {
        if (!TryCreate(symbol, timeFrame, timeZone, provider, bars, out HistoricalSeries? series, out IReadOnlyList<string> errors))
        {
            throw new ArgumentException(
                $"HistoricalSeries is invalid and is never silently repaired. Violations: {string.Join(" | ", errors)}");
        }

        return series;
    }

    /// <summary>
    /// Validates and builds without throwing. <paramref name="series"/> is non-null only when the
    /// method returns true; <paramref name="errors"/> is empty only in that same case.
    ///
    /// Checks, in order: identity fields non-empty, at least one bar, then per bar: structural validity
    /// (<see cref="HistoricalBar.Validate"/>), then strictly increasing timestamps. A repeated timestamp
    /// and an out-of-order timestamp are reported as two DISTINCT violations rather than merged, because
    /// they indicate different upstream defects (a duplicated export row vs. an unsorted feed).
    /// </summary>
    public static bool TryCreate(
        string symbol,
        string timeFrame,
        string timeZone,
        string provider,
        IReadOnlyList<HistoricalBar> bars,
        out HistoricalSeries series,
        out IReadOnlyList<string> errors)
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(symbol))
            violations.Add("Symbol is required.");

        if (string.IsNullOrWhiteSpace(timeFrame))
            violations.Add("TimeFrame is required.");

        if (string.IsNullOrWhiteSpace(timeZone))
            violations.Add("TimeZone is required - it is never defaulted (see HistoricalSeries doc comment).");

        if (string.IsNullOrWhiteSpace(provider))
            violations.Add("Provider is required.");

        if (bars is null || bars.Count == 0)
        {
            violations.Add("At least one bar is required.");
            series = null!;
            errors = violations;
            return false;
        }

        for (int i = 0; i < bars.Count; i++)
        {
            HistoricalBar bar = bars[i];

            foreach (string barError in bar.Validate())
                violations.Add($"Bar[{i}]: {barError}");

            if (i == 0)
                continue;

            DateTime previous = bars[i - 1].Timestamp;
            DateTime current = bar.Timestamp;

            if (current == previous)
            {
                violations.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Bar[{i}]: duplicate timestamp {current:O} (same as Bar[{i - 1}]). Duplicates are never removed automatically."));
            }
            else if (current < previous)
            {
                violations.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Bar[{i}]: timestamp {current:O} is earlier than Bar[{i - 1}] ({previous:O}). The series is never sorted automatically."));
            }
        }

        if (violations.Count > 0)
        {
            series = null!;
            errors = violations;
            return false;
        }

        // Defensive copy: the caller's list must never be able to mutate an already-validated series.
        var snapshot = new HistoricalBar[bars.Count];
        for (int i = 0; i < bars.Count; i++)
            snapshot[i] = bars[i];

        series = new HistoricalSeries(symbol, timeFrame, timeZone, provider, snapshot);
        errors = Array.Empty<string>();
        return true;
    }
}
