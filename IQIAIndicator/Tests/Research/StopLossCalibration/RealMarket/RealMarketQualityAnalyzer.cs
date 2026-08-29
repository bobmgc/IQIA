using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.RealMarket;

/// <summary>
/// Sprint 15.18 (QDE-012 real-market data quality). Pure, read-only analysis of an already-parsed
/// real-market OHLCV bar sequence (RealMarketOhlcvCsvReader output). Every method here only computes
/// and returns facts - it never repairs, drops, reorders, or fabricates a bar, per the sprint's "raw
/// data is evidence" rule. Deliberately does NOT assign a PASS/FAIL verdict to any dimension: that
/// judgment (documented, with reasoning) is made once, explicitly, in
/// RealMarketQualityGateDecisions - see that file's doc comment for why the split exists.
/// </summary>
public static class RealMarketQualityAnalyzer
{
    // ── Section 11: session / symbol / timeframe integrity ──────────────────────────────────────────

    public static SessionIntegrityFacts AnalyzeSessionIntegrity(IReadOnlyList<RealMarketBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (bars.Count == 0)
            return new SessionIntegrityFacts(0, Array.Empty<Guid>(), Array.Empty<string>(), Array.Empty<string>());

        Guid[] sessionIds = bars.Select(b => b.SessionId).Distinct().ToArray();
        string[] symbols = bars.Select(b => b.Symbol).Distinct(StringComparer.Ordinal).ToArray();
        string[] timeFrames = bars.Select(b => b.TimeFrame).Distinct(StringComparer.Ordinal).ToArray();
        return new SessionIntegrityFacts(bars.Count, sessionIds, symbols, timeFrames);
    }

    // ── Section 5: timestamp quality ─────────────────────────────────────────────────────────────────

    public static TimestampFacts AnalyzeTimestamps(IReadOnlyList<RealMarketBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (bars.Count == 0)
            throw new InvalidOperationException("Cannot analyze timestamps of an empty bar sequence.");

        bool strictlyIncreasing = true;
        bool nonDecreasing = true;
        int duplicateTimestamps = 0;
        var seen = new HashSet<DateTime>();
        var intervals = new List<double>(bars.Count - 1);

        for (int i = 0; i < bars.Count; i++)
        {
            if (!seen.Add(bars[i].Timestamp))
                duplicateTimestamps++;

            if (i > 0)
            {
                double seconds = (bars[i].Timestamp - bars[i - 1].Timestamp).TotalSeconds;
                intervals.Add(seconds);
                if (seconds <= 0)
                    strictlyIncreasing = false;
                if (seconds < 0)
                    nonDecreasing = false;
            }
        }

        return new TimestampFacts(
            FirstTimestamp: bars[0].Timestamp,
            LastTimestamp: bars[^1].Timestamp,
            StrictlyIncreasing: strictlyIncreasing,
            NonDecreasing: nonDecreasing,
            DuplicateTimestampCount: duplicateTimestamps,
            Intervals: intervals);
    }

    /// <summary>Parses an ATAS-style TimeFrame string ("M5", "M1", "H1", "D1") into its expected bar
    /// spacing. Returns null (never guesses) if the TimeFrame doesn't match this shape - callers must
    /// fall back to the modal observed interval rather than assume a spacing this method cannot
    /// establish from the data itself.</summary>
    public static TimeSpan? TryParseExpectedInterval(string timeFrame)
    {
        if (string.IsNullOrWhiteSpace(timeFrame) || timeFrame.Length < 2)
            return null;

        char unit = char.ToUpperInvariant(timeFrame[0]);
        if (!int.TryParse(timeFrame.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount <= 0)
            return null;

        return unit switch
        {
            'M' => TimeSpan.FromMinutes(amount),
            'H' => TimeSpan.FromHours(amount),
            'D' => TimeSpan.FromDays(amount),
            _ => null
        };
    }

    /// <summary>Every interval strictly greater than expectedInterval is a gap. Classification is a
    /// generic, size-only heuristic (whole-multiple-of-expected-interval, then bucketed by absolute
    /// size) - it does NOT encode any exchange-specific trading calendar, because no such calendar
    /// exists anywhere in this repository (QDE-012 Sprint 15.16/15.18 audits). Anything this heuristic
    /// cannot bucket confidently is UNRESOLVED rather than guessed.</summary>
    public static IReadOnlyList<GapRecord> ClassifyGaps(IReadOnlyList<RealMarketBar> bars, TimeSpan expectedInterval)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (expectedInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(expectedInterval));

        double expectedSeconds = expectedInterval.TotalSeconds;
        var gaps = new List<GapRecord>();

        for (int i = 1; i < bars.Count; i++)
        {
            double seconds = (bars[i].Timestamp - bars[i - 1].Timestamp).TotalSeconds;
            if (seconds <= expectedSeconds)
                continue;

            bool isExactMultiple = Math.Abs((seconds / expectedSeconds) - Math.Round(seconds / expectedSeconds)) < 1e-6;
            int gapBars = (int)Math.Round(seconds / expectedSeconds) - 1; // bars that would be "missing" if spacing were regular

            string classification = !isExactMultiple
                ? "IRREGULAR_INTERVAL_UNRESOLVED"
                : seconds >= TimeSpan.FromHours(20).TotalSeconds
                    ? "LARGE_GAP_PLAUSIBLE_MULTI_DAY_CLOSURE"
                    : seconds >= expectedSeconds * 2 && seconds <= TimeSpan.FromHours(4).TotalSeconds
                        ? "MEDIUM_GAP_PLAUSIBLE_INTRADAY_CLOSURE"
                        : "UNRESOLVED_GAP_SIZE";

            gaps.Add(new GapRecord(bars[i - 1].Timestamp, bars[i].Timestamp, seconds, gapBars, classification));
        }

        return gaps;
    }

    // ── Section 6/7: OHLC + volume validation ────────────────────────────────────────────────────────

    public static OhlcValidationFacts ValidateOhlc(IReadOnlyList<RealMarketBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);

        int structuralViolations = 0;
        int nonPositivePrice = 0;
        int negativeVolume = 0;
        int zeroVolume = 0;
        int identicalOhlc = 0;
        var ranges = new List<decimal>(bars.Count);

        foreach (RealMarketBar bar in bars)
        {
            bool violated = false;
            if (bar.High < Math.Max(bar.Open, bar.Close)) violated = true;
            if (bar.Low > Math.Min(bar.Open, bar.Close)) violated = true;
            if (bar.High < bar.Low) violated = true;
            if (violated) structuralViolations++;

            if (bar.Open <= 0m || bar.High <= 0m || bar.Low <= 0m || bar.Close <= 0m) nonPositivePrice++;
            if (bar.Volume < 0m) negativeVolume++;
            if (bar.Volume == 0m) zeroVolume++;
            if (bar.Open == bar.High && bar.High == bar.Low && bar.Low == bar.Close) identicalOhlc++;

            ranges.Add(bar.High - bar.Low);
        }

        return new OhlcValidationFacts(
            TotalBars: bars.Count,
            StructuralViolations: structuralViolations,
            // decimal has no NaN/Infinity representation - RealMarketOhlcvCsvReader.Read() would throw
            // FormatException while parsing such a token rather than silently produce one, so this is
            // always 0 by construction for any bar sequence this analyzer can even hold. Recorded
            // explicitly (not omitted) so the report states this as a checked fact, not an assumption.
            NaNOrInfiniteCount: 0,
            NonPositivePriceCount: nonPositivePrice,
            NegativeVolumeCount: negativeVolume,
            ZeroVolumeCount: zeroVolume,
            IdenticalOhlcCount: identicalOhlc,
            Ranges: ranges);
    }

    public static VolumeFacts AnalyzeVolume(IReadOnlyList<RealMarketBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (bars.Count == 0)
            throw new InvalidOperationException("Cannot analyze volume of an empty bar sequence.");

        decimal[] volumes = bars.Select(b => b.Volume).OrderBy(v => v).ToArray();
        return new VolumeFacts(
            Min: volumes[0],
            Max: volumes[^1],
            Median: Median(volumes),
            Mean: volumes.Average(),
            ZeroCount: volumes.Count(v => v == 0m),
            NegativeCount: volumes.Count(v => v < 0m));
    }

    /// <summary>Close-to-close returns computed ONLY between chronologically adjacent bars whose gap
    /// equals expectedInterval - i.e. never across a session/market-closure gap, so a legitimate
    /// weekend or maintenance-break price change is never reported as an intrabar "return". Bars on
    /// either side of a gap are simply excluded from this list, not zero-filled or interpolated.</summary>
    public static IReadOnlyList<double> AnalyzeConsecutiveReturns(IReadOnlyList<RealMarketBar> bars, TimeSpan expectedInterval)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var returns = new List<double>();
        for (int i = 1; i < bars.Count; i++)
        {
            if (bars[i].Timestamp - bars[i - 1].Timestamp != expectedInterval)
                continue;
            if (bars[i - 1].Close == 0m)
                continue;
            returns.Add((double)((bars[i].Close - bars[i - 1].Close) / bars[i - 1].Close));
        }
        return returns;
    }

    // ── Section 9: continuity / coverage ─────────────────────────────────────────────────────────────

    public static ContinuityFacts AnalyzeContinuity(IReadOnlyList<RealMarketBar> bars, TimeSpan expectedInterval)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (bars.Count == 0)
            return new ContinuityFacts(0, 0, 0, Array.Empty<(int Start, int End)>());

        var runs = new List<(int Start, int End)>();
        int runStart = 0;
        for (int i = 1; i < bars.Count; i++)
        {
            if (bars[i].Timestamp - bars[i - 1].Timestamp != expectedInterval)
            {
                runs.Add((runStart, i - 1));
                runStart = i;
            }
        }
        runs.Add((runStart, bars.Count - 1));

        int uniqueTimestamps = bars.Select(b => b.Timestamp).Distinct().Count();
        int uniqueCurrentBars = bars.Select(b => b.CurrentBar).Distinct().Count();

        return new ContinuityFacts(bars.Count, uniqueTimestamps, uniqueCurrentBars, runs);
    }

    // ── Section 13: 40-bar horizon adequacy ──────────────────────────────────────────────────────────

    /// <summary>A "usable window" needs `horizon` bars strictly after its anchor, all within the SAME
    /// continuous run (never crossing a timestamp gap) - i.e. a run of length L contributes
    /// max(0, L - horizon) usable anchors. `runs` must come from AnalyzeContinuity on the same bar
    /// sequence/expectedInterval.</summary>
    public static HorizonAdequacyFacts AnalyzeHorizonAdequacy(int totalBars, IReadOnlyList<(int Start, int End)> runs, int horizon)
    {
        if (horizon <= 0)
            throw new ArgumentOutOfRangeException(nameof(horizon));

        int usable = 0;
        int longestRun = 0;
        foreach ((int start, int end) in runs)
        {
            int length = end - start + 1;
            longestRun = Math.Max(longestRun, length);
            if (length > horizon)
                usable += length - horizon;
        }

        int theoretical = Math.Max(0, totalBars - horizon);
        int blocked = theoretical - usable;
        double percentUsable = theoretical == 0 ? 0.0 : 100.0 * usable / theoretical;

        return new HorizonAdequacyFacts(horizon, theoretical, usable, blocked, percentUsable, longestRun, runs.Count);
    }

    // ── Section 4: forming-bar-capture evidence ──────────────────────────────────────────────────────

    /// <summary>Identifies contiguous runs of bars where Open==High==Low==Close (zero intrabar range) -
    /// i.e. bars whose retained OHLCV snapshot is consistent with having captured only a single print/
    /// tick of that bar's formation, given ScientificDatasetCollector's documented first-Add()-wins
    /// dedup identity (SessionId+Symbol+TimeFrame+CurrentBar - see ScientificDatasetCollector.cs's
    /// class doc comment). A single isolated zero-range bar can be genuine (a real, thin print with no
    /// further trades before close); a LONG contiguous run of them - especially one that also shows
    /// very low Volume, checked separately by the caller/report, not by this method - is the direct,
    /// data-level signature of the FORMING_BAR_CAPTURE risk the Sprint 15.18 brief asks to investigate.
    /// This method only detects and reports the runs; it forms no verdict.</summary>
    public static IReadOnlyList<FormingBarSegment> DetectZeroRangeSegments(IReadOnlyList<RealMarketBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var segments = new List<FormingBarSegment>();
        int i = 0;
        while (i < bars.Count)
        {
            if (!IsZeroRange(bars[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < bars.Count && IsZeroRange(bars[i]))
                i++;
            int end = i - 1;

            decimal[] volumesInSegment = bars.Skip(start).Take(end - start + 1).Select(b => b.Volume).ToArray();
            segments.Add(new FormingBarSegment(
                start,
                end,
                bars[start].Timestamp,
                bars[end].Timestamp,
                end - start + 1,
                volumesInSegment.Min(),
                volumesInSegment.Max(),
                Median(volumesInSegment)));
        }
        return segments;
    }

    private static bool IsZeroRange(RealMarketBar bar) =>
        bar.Open == bar.High && bar.High == bar.Low && bar.Low == bar.Close;

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        decimal[] sorted = values.OrderBy(v => v).ToArray();
        int n = sorted.Length;
        if (n == 0) return 0m;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2m;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        double[] sorted = values.OrderBy(v => v).ToArray();
        int n = sorted.Length;
        if (n == 0) return 0.0;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    // ── Section 10: timezone ─────────────────────────────────────────────────────────────────────────

    /// <summary>The live pipeline (Core/MarketContextBuilder.cs) passes ATAS's IndicatorCandle.Time
    /// through with no TimeZoneInfo/DateTimeKind conversion anywhere (QDE-012 Sprint 15.16/15.17
    /// audits), and ScientificDatasetSessionWriter records this honestly as
    /// "Unspecified (ATAS IndicatorCandle.Time passthrough, no conversion applied)" in every metadata
    /// file it writes (see ScientificDatasetSessionWriter.UnspecifiedTimezone). This method never
    /// invents or infers a timezone from the description string - it only recognizes the honest
    /// "Unspecified" self-report and returns Unresolved, per the brief's explicit instruction not to
    /// fabricate a timezone.</summary>
    public static QualityStatus ClassifyTimezone(string timezoneDescription)
    {
        ArgumentNullException.ThrowIfNull(timezoneDescription);
        return timezoneDescription.Contains("Unspecified", StringComparison.OrdinalIgnoreCase)
            ? QualityStatus.Unresolved
            : QualityStatus.Pass;
    }

    // ── shared descriptive stats used by the report/CSV writer ──────────────────────────────────────

    public static (double Min, double Max, double Median, double Mean, double StdDevPopulation) DescribeStats(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return (0, 0, 0, 0, 0);
        double mean = values.Average();
        double variance = values.Select(v => (v - mean) * (v - mean)).Average();
        return (values.Min(), values.Max(), Median(values), mean, Math.Sqrt(variance));
    }

    public static (decimal Min, decimal Max, decimal Median, decimal Mean) DescribeStats(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
            return (0m, 0m, 0m, 0m);
        return (values.Min(), values.Max(), Median(values), values.Average());
    }
}

public sealed record SessionIntegrityFacts(int RowCount, IReadOnlyList<Guid> DistinctSessionIds, IReadOnlyList<string> DistinctSymbols, IReadOnlyList<string> DistinctTimeFrames)
{
    public bool SingleSession => DistinctSessionIds.Count == 1;
    public bool SingleSymbol => DistinctSymbols.Count == 1;
    public bool SingleTimeFrame => DistinctTimeFrames.Count == 1;
}

public sealed record TimestampFacts(
    DateTime FirstTimestamp,
    DateTime LastTimestamp,
    bool StrictlyIncreasing,
    bool NonDecreasing,
    int DuplicateTimestampCount,
    IReadOnlyList<double> Intervals);

public sealed record GapRecord(DateTime From, DateTime To, double GapSeconds, int GapBars, string Classification);

public sealed record OhlcValidationFacts(
    int TotalBars,
    int StructuralViolations,
    int NaNOrInfiniteCount,
    int NonPositivePriceCount,
    int NegativeVolumeCount,
    int ZeroVolumeCount,
    int IdenticalOhlcCount,
    IReadOnlyList<decimal> Ranges);

public sealed record VolumeFacts(decimal Min, decimal Max, decimal Median, decimal Mean, int ZeroCount, int NegativeCount);

public sealed record ContinuityFacts(int TotalBars, int UniqueTimestamps, int UniqueCurrentBars, IReadOnlyList<(int Start, int End)> Runs);

public sealed record HorizonAdequacyFacts(int Horizon, int TheoreticalWindows, int UsableWindows, int BlockedByGaps, double PercentUsable, int LongestRunLength, int RunCount);

public sealed record FormingBarSegment(int StartIndex, int EndIndex, DateTime StartTimestamp, DateTime EndTimestamp, int BarCount, decimal MinVolumeInSegment, decimal MaxVolumeInSegment, decimal MedianVolumeInSegment);
