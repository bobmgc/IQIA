using System;
using System.Globalization;
using System.Text;
using IQIAIndicator.Core.MarketData;

namespace IQIAIndicator.Backtest;

/// <summary>
/// Sprint 15.25 (Lot 14.2, brief §18). Deterministic identity fingerprint for any
/// <see cref="HistoricalSeries"/> - Yahoo-sourced or otherwise. Depends only on the data actually
/// ingested (Symbol/TimeFrame/TimeZone/Provider/bar count + every bar's Timestamp/OHLCV and
/// BidVolume/AskVolume/Delta/OpenInterest when present) - never on download time, machine identity, user
/// identity, or a request URL, exactly as required (brief §18: "NE PAS utiliser : heure de
/// téléchargement ; machine ; utilisateur ; URL seule").
///
/// PLACEMENT AND REUSE: the brief asks to reuse <see cref="BacktestFingerprint"/> from Lot 14.1 "si
/// possible". That class's canonicalization helpers (<c>D</c>/<c>Dt</c>) are private and scoped to
/// scenario/bar/evidence fingerprinting - not to a bare HistoricalSeries - and
/// <see cref="Core.MarketData"/> must not depend on <see cref="IQIAIndicator.Backtest"/> (Core is the
/// lower layer; Backtest already depends on Core, not the reverse - inverting that for a single formatter
/// would be the wrong trade). This class lives in <c>Backtest</c> instead (which already legitimately
/// depends on <c>Core.MarketData</c>) and reuses the ONE piece of <see cref="BacktestFingerprint"/> that
/// is both public and provider-agnostic: <see cref="BacktestFingerprint.Sha256Hex"/>, the exact same
/// SHA-256 primitive <see cref="BacktestFoundationResult.DeterministicHash"/> is built from. Nothing in
/// <c>BacktestFingerprint.cs</c> was modified to make this possible (Lot 14.2 brief §27 keeps it stable).
/// </summary>
public static class HistoricalSeriesFingerprint
{
    public static string Compute(HistoricalSeries series)
    {
        var sb = new StringBuilder();
        sb.Append("Symbol=").Append(series.Symbol).Append('|');
        sb.Append("TimeFrame=").Append(series.TimeFrame).Append('|');
        sb.Append("TimeZone=").Append(series.TimeZone).Append('|');
        sb.Append("Provider=").Append(series.Provider).Append('|');
        sb.Append("Count=").Append(series.Count).Append('\n');

        foreach (HistoricalBar bar in series.Bars)
        {
            sb.Append("ts=").Append(Dt(bar.Timestamp));
            sb.Append(" O=").Append(D(bar.Open));
            sb.Append(" H=").Append(D(bar.High));
            sb.Append(" L=").Append(D(bar.Low));
            sb.Append(" C=").Append(D(bar.Close));
            sb.Append(" V=").Append(D(bar.Volume));
            sb.Append(" Bid=").Append(N(bar.BidVolume));
            sb.Append(" Ask=").Append(N(bar.AskVolume));
            sb.Append(" Delta=").Append(N(bar.Delta));
            sb.Append(" OI=").Append(N(bar.OpenInterest));
            sb.Append('\n');
        }

        return BacktestFingerprint.Sha256Hex(sb.ToString());
    }

    private static string D(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string N(decimal? value) => value is decimal v ? D(v) : "null";

    private static string Dt(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);
}
