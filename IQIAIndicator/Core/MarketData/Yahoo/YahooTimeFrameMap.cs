using System;
using System.Collections.Generic;

namespace IQIAIndicator.Core.MarketData.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 14.2, brief §8). Explicit, closed mapping from an IQIA TimeFrame string (the same
/// convention already used throughout this codebase - e.g. ChartInfo.TimeFrame, "M5"/"M1"/"H1"/"D1", see
/// Tests/Research/StopLossCalibration/RealMarket/RealMarketQualityAnalyzer.TryParseExpectedInterval) to
/// the interval string Yahoo's chart API expects.
///
/// ONLY "M5" is supported in this lot (brief §8: "Commencer par : 5m ... Ne pas prétendre supporter 1m/
/// 15m/1h/1d/etc. sans implémentation et test réels"). Every other TimeFrame throws - never a silent
/// fallback to a different interval (brief §8's explicit forbidden example: "demande 5m -&gt; Yahoo 15m
/// -&gt; continuer quand même").
/// </summary>
internal static class YahooTimeFrameMap
{
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["M5"] = "5m",
    };

    public static string ResolveYahooInterval(string iqiaTimeFrame)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iqiaTimeFrame);

        if (Map.TryGetValue(iqiaTimeFrame, out string? interval))
            return interval;

        throw new NotSupportedException(
            $"IQIA TimeFrame '{iqiaTimeFrame}' has no supported Yahoo interval mapping in this lot. " +
            $"Only {string.Join(", ", Map.Keys)} is implemented and tested - never falls back to a different interval.");
    }

    /// <summary>Every IQIA TimeFrame this map currently accepts - exposed for tests/diagnostics, never
    /// used to guess an unmapped value.</summary>
    public static IReadOnlyCollection<string> SupportedTimeFrames => (IReadOnlyCollection<string>)Map.Keys;
}
