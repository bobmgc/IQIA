using System;
using IQIAIndicator.Core.MarketData;

namespace IQIAIndicator.Core;

/// <summary>
/// Sprint 15.25 (Lot 14.1, brief §7/§8; Lot 13 report §6.3 - the single recommended modification of that
/// audit). THE one place where a <see cref="MarketContext"/> is assembled, whatever the data source.
///
/// WHY IT EXISTS: before this lot, the mapping "raw bar -&gt; MarketContext" (Median, TypicalPrice,
/// ElapsedMinutes, the empty SessionInfo, the MarketClock/ExecutionContext shape) lived exclusively
/// inside <see cref="MarketContextBuilder.Build"/>, reachable only through an
/// <c>ATAS.Indicators.IndicatorCandle</c>. A Backtest Engine would have had to restate those formulas,
/// and any later correction applied to one copy and not the other would silently make backtest and live
/// disagree - exactly the "second IQIA" the Lot 13 audit was written to prevent (report §17, RISK-07).
///
/// WHAT IT DOES NOT DO: it decides nothing about the PLATFORM. Whether a bar is first/last/realtime/
/// historical/replay is computed by the CALLER and passed in explicitly, because those answers are
/// genuinely different per host (ATAS derives them from bar/currentBar and a replay heuristic; a
/// backtest knows them from its own position in a finite series). Only the arithmetic and the object
/// shape are shared. This is deliberate: sharing the heuristics too would have forced the backtest to
/// adopt the ATAS replay heuristic, which is meaningless offline.
///
/// ATAS-FREE by construction - this file references only Core types.
/// </summary>
public static class MarketContextFactory
{
    /// <summary>Median price of a bar: (High + Low) / 2. Identical to the expression
    /// <see cref="MarketContextBuilder.Build"/> used before this extraction.</summary>
    public static decimal Median(decimal high, decimal low) => (high + low) / 2m;

    /// <summary>Typical price of a bar: (High + Low + Close) / 3. Identical to the expression
    /// <see cref="MarketContextBuilder.Build"/> used before this extraction.</summary>
    public static decimal TypicalPrice(decimal high, decimal low, decimal close) => (high + low + close) / 3m;

    /// <summary>Whole minutes elapsed since the session's first bar; 0 on the first bar itself. Uses
    /// <c>(int)TimeSpan.TotalMinutes</c> - truncation toward zero - exactly as before this extraction.</summary>
    public static int ElapsedMinutes(DateTime barTime, DateTime firstBarTime, bool isFirstBar) =>
        isFirstBar ? 0 : (int)(barTime - firstBarTime).TotalMinutes;

    /// <summary>
    /// The session placeholder the pipeline has always used. <see cref="MarketContextBuilder"/> has
    /// written exactly this since Sprint 1: no session/timezone capability exists anywhere in this
    /// codebase (confirmed by the Sprint 15.16 audit and re-confirmed by the Lot 13 audit, §3.2b).
    /// Reproduced verbatim so a backtest context is indistinguishable from a live one on this field -
    /// inventing session boundaries here would be a DIVERGENCE from production, not an improvement
    /// (Lot 14.1 brief §7, "NE PAS inventer de logique de session").
    /// </summary>
    public static SessionInfo UnknownSession() => new(string.Empty, DateTime.MinValue, DateTime.MaxValue);

    /// <summary>
    /// Canonical assembly of a <see cref="MarketContext"/>. Every platform-dependent flag is an explicit
    /// parameter; every derived price/time value is computed here and nowhere else.
    /// </summary>
    /// <param name="barIndex">Index of the bar being built. Becomes <c>ExecutionContext.LastCalculatedBar</c>.</param>
    /// <param name="currentBar">Host's own bar count. Becomes <c>ExecutionContext.CurrentBar</c>.</param>
    /// <param name="firstBarTime">Timestamp of the run's very first bar, for <see cref="ElapsedMinutes"/>. Ignored when <paramref name="isFirstBar"/> is true.</param>
    public static MarketContext Create(
        int barIndex,
        int currentBar,
        string timeFrame,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume,
        decimal bidVolume,
        decimal askVolume,
        decimal delta,
        InstrumentInfo instrument,
        DateTime barTime,
        DateTime firstBarTime,
        bool isFirstBar,
        bool isLastBar,
        bool isRealtime,
        bool isHistorical,
        bool isReplay)
    {
        return new MarketContext
        {
            BarIndex = barIndex,
            TimeFrame = timeFrame,
            Price = new PriceInfo(
                open,
                high,
                low,
                close,
                Median(high, low),
                TypicalPrice(high, low, close)),
            Volume = new VolumeInfo(volume, bidVolume, askVolume, delta),
            Instrument = instrument,
            Clock = new MarketClock
            {
                CurrentTime = barTime,
                CurrentDate = DateOnly.FromDateTime(barTime),
                DayOfWeek = barTime.DayOfWeek,
                Session = UnknownSession(),
                ElapsedMinutes = ElapsedMinutes(barTime, firstBarTime, isFirstBar),
                IsFirstBar = isFirstBar,
                IsLastBar = isLastBar
            },
            Execution = new ExecutionContext
            {
                CurrentBar = currentBar,
                LastCalculatedBar = barIndex,
                IsRealtime = isRealtime,
                IsHistorical = isHistorical,
                IsReplay = isReplay
            }
        };
    }

    /// <summary>
    /// Backtest-side convenience over <see cref="Create"/>, applying the historical-replay conventions
    /// fixed by the Lot 14.1 brief §7:
    /// <c>CurrentBar = index + 1</c>, <c>IsRealtime = false</c>, <c>IsHistorical = true</c>,
    /// <c>IsReplay = false</c>, <c>IsFirstBar = index == 0</c>, <c>IsLastBar = index == barCount - 1</c>.
    ///
    /// <c>IsReplay</c> is hard-wired to false on purpose: ATAS "Replay" is a live-platform playback mode
    /// whose detection heuristic (bar-index based, see MarketContextBuilder) has no meaning offline. A
    /// backtest is historical, not replayed.
    ///
    /// ABSENT MICROSTRUCTURE FIELDS: <see cref="HistoricalBar"/> keeps BidVolume/AskVolume/Delta nullable
    /// so a provider that does not publish them leaves them genuinely absent. <see cref="VolumeInfo"/>
    /// (unchanged, Sprint 1) has no nullable representation, so the projection uses 0m. This is a
    /// PROJECTION LIMIT, not a fabricated reading, and it is inert: the Lot 13 audit established that no
    /// evidence model, no scientific model and no decision rule reads VolumeInfo at all - only the
    /// scientific dataset does. The absence remains observable on the source <see cref="HistoricalBar"/>.
    /// </summary>
    public static MarketContext CreateHistorical(
        HistoricalBar bar,
        int index,
        int barCount,
        string timeFrame,
        InstrumentInfo instrument,
        DateTime firstBarTimestamp)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Bar index cannot be negative.");

        if (barCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(barCount), barCount, "Bar count must be positive.");

        if (index >= barCount)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Bar index must be below barCount ({barCount}).");

        return Create(
            barIndex: index,
            currentBar: index + 1,
            timeFrame: timeFrame,
            open: bar.Open,
            high: bar.High,
            low: bar.Low,
            close: bar.Close,
            volume: bar.Volume,
            bidVolume: bar.BidVolume ?? 0m,
            askVolume: bar.AskVolume ?? 0m,
            delta: bar.Delta ?? 0m,
            instrument: instrument,
            barTime: bar.Timestamp,
            firstBarTime: firstBarTimestamp,
            isFirstBar: index == 0,
            isLastBar: index == barCount - 1,
            isRealtime: false,
            isHistorical: true,
            isReplay: false);
    }
}
