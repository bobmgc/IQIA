using System;
using System.Collections.Generic;
using System.Globalization;

namespace IQIAIndicator.Core.MarketData;

/// <summary>
/// Sprint 15.25 (Lot 14.1, brief §3). One historical OHLCV bar, independent of any data provider and of
/// ATAS. Deliberately carries NO Symbol/TimeFrame/Provider/SessionId/CurrentBar: those belong to the
/// series (<see cref="HistoricalSeries"/>) or the scenario, and repeating them per bar is what makes a
/// self-inconsistent series possible (the pre-existing Tests-side
/// <c>Research/StopLossCalibration/RealMarket/RealMarketBar</c> does carry them, because it mirrors an
/// export CSV's column order rather than a domain contract - see the Lot 13 report §7.1).
///
/// Shaped as a <c>readonly record struct</c> to match the existing Core value models
/// (<see cref="PriceInfo"/>, <see cref="VolumeInfo"/>, <see cref="InstrumentInfo"/>,
/// <see cref="SessionInfo"/>) rather than introducing a second convention.
///
/// NOTHING IS EVER FABRICATED HERE. <see cref="BidVolume"/>/<see cref="AskVolume"/>/<see cref="Delta"/>/
/// <see cref="OpenInterest"/> are nullable precisely because a provider that does not publish them
/// (e.g. a daily/intraday OHLCV feed) must leave them absent - never zero-filled, which would be
/// indistinguishable from a genuine zero reading.
///
/// VALIDATION IS DETECTABLE, NOT ENFORCED IN THE CONSTRUCTOR: C# always permits <c>default(T)</c> for a
/// struct, so a throwing constructor could never actually guarantee the invariants. <see cref="Validate"/>
/// reports every violation and <see cref="HistoricalSeries"/> is the component that REJECTS on them
/// (Lot 14.1 brief §3/§4) - a bad bar is surfaced, never silently repaired.
/// </summary>
public readonly record struct HistoricalBar(
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal? BidVolume = null,
    decimal? AskVolume = null,
    decimal? Delta = null,
    decimal? OpenInterest = null)
{
    /// <summary>True only when <see cref="Validate"/> reports no violation at all.</summary>
    public bool IsValid => Validate().Count == 0;

    /// <summary>
    /// Every invariant violation on this bar, in a fixed order, as human-readable diagnostics. Empty
    /// means the bar is structurally usable. The rule set is a deliberate SUPERSET of what
    /// <see cref="MarketContextValidator"/> already enforces downstream on the derived
    /// <see cref="MarketContext"/> (Open &gt; 0, Close &gt; 0, High &gt;= Low, Volume &gt;= 0) - it adds
    /// High &gt; 0, Low &gt; 0 and a non-default Timestamp, so a malformed historical row is caught at
    /// ingestion time rather than one stage later, where the message would no longer name the source row.
    ///
    /// Deliberately NOT checked here: High &gt;= max(Open, Close) and Low &lt;= min(Open, Close).
    /// MarketContextValidator treats those two as WARNINGS, not errors (see its CheckPrice), and this
    /// type must not be stricter than the pipeline it feeds - that would reject bars ATAS itself accepts.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Timestamp == default)
            errors.Add("Timestamp is unset (default(DateTime)).");

        if (Open <= 0m)
            errors.Add(Format("Open must be positive.", nameof(Open), Open));

        if (High <= 0m)
            errors.Add(Format("High must be positive.", nameof(High), High));

        if (Low <= 0m)
            errors.Add(Format("Low must be positive.", nameof(Low), Low));

        if (Close <= 0m)
            errors.Add(Format("Close must be positive.", nameof(Close), Close));

        if (High < Low)
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"High ({High}) is below Low ({Low})."));

        if (Volume < 0m)
            errors.Add(Format("Volume cannot be negative.", nameof(Volume), Volume));

        return errors;
    }

    private static string Format(string message, string field, decimal value) =>
        string.Create(CultureInfo.InvariantCulture, $"{message} {field}={value}.");
}
