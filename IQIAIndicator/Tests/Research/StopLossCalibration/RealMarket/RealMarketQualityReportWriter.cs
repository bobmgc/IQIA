using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.RealMarket;

/// <summary>Sprint 15.18. Per-dimension verdicts (Section 14 of the brief: "report independent
/// dimensions... do not combine into one numerical score"). Deliberately just an enum + a short,
/// human-authored Summary string per dimension - never a weighted/composite score.</summary>
public enum QualityStatus { Pass, PassWithCaveats, Fail, Unresolved }

public sealed record DimensionVerdict(string Dimension, QualityStatus Status, string Summary);

/// <summary>
/// Sprint 15.18 (QDE-012 real-market data quality). Writes the analysis facts produced by
/// RealMarketQualityAnalyzer to the CSV/txt files required by the brief's Section 18. This class only
/// serializes already-computed facts and already-decided DimensionVerdicts (decided explicitly, with
/// reasoning, by the caller/report - see Sprint1518RealCaptureQualityTests and the Sprint 15.18 report,
/// Section 15) - it makes no quality judgment of its own.
/// </summary>
public static class RealMarketQualityReportWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteSummary(string path, IReadOnlyList<DimensionVerdict> verdicts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Dimension,Status,Summary");
        foreach (DimensionVerdict v in verdicts)
            sb.AppendLine(string.Join(",", v.Dimension, v.Status.ToString(), Escape(v.Summary)));
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    public static void WriteTimestampAnalysis(string path, TimestampFacts facts, TimeSpan expectedInterval, IReadOnlyList<GapRecord> gaps)
    {
        var (min, max, median, mean, stdDev) = RealMarketQualityAnalyzer.DescribeStats(facts.Intervals);
        var modal = facts.Intervals.Count == 0
            ? 0.0
            : facts.Intervals.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;

        var sb = new StringBuilder();
        sb.AppendLine("Metric,Value");
        sb.AppendLine($"FirstTimestamp,{facts.FirstTimestamp:O}");
        sb.AppendLine($"LastTimestamp,{facts.LastTimestamp:O}");
        sb.AppendLine($"ElapsedCalendarSeconds,{(facts.LastTimestamp - facts.FirstTimestamp).TotalSeconds.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"ExpectedIntervalSeconds,{expectedInterval.TotalSeconds.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"StrictlyIncreasing,{facts.StrictlyIncreasing}");
        sb.AppendLine($"NonDecreasing,{facts.NonDecreasing}");
        sb.AppendLine($"DuplicateTimestampCount,{facts.DuplicateTimestampCount}");
        sb.AppendLine($"IntervalCount,{facts.Intervals.Count}");
        sb.AppendLine($"MinIntervalSeconds,{min.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"MaxIntervalSeconds,{max.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"MedianIntervalSeconds,{median.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"MeanIntervalSeconds,{mean.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"ModalIntervalSeconds,{modal.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"StdDevIntervalSeconds,{stdDev.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"GapCount,{gaps.Count}");
        sb.AppendLine($"LargestGapSeconds,{(gaps.Count == 0 ? 0.0 : gaps.Max(g => g.GapSeconds)).ToString(CultureInfo.InvariantCulture)}");
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    public static void WriteGapAnalysis(string path, IReadOnlyList<GapRecord> gaps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FromTimestamp,ToTimestamp,GapSeconds,GapMinutes,GapBarsMissingIfRegular,Classification");
        foreach (GapRecord g in gaps)
        {
            sb.AppendLine(string.Join(",",
                g.From.ToString("O", CultureInfo.InvariantCulture),
                g.To.ToString("O", CultureInfo.InvariantCulture),
                g.GapSeconds.ToString(CultureInfo.InvariantCulture),
                (g.GapSeconds / 60.0).ToString(CultureInfo.InvariantCulture),
                g.GapBars.ToString(CultureInfo.InvariantCulture),
                g.Classification));
        }
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    public static void WriteOhlcvValidation(string path, OhlcValidationFacts facts, IReadOnlyList<double> consecutiveReturns, IReadOnlyList<FormingBarSegment> zeroRangeSegments)
    {
        var (rangeMin, rangeMax, rangeMedian, rangeMean) = RealMarketQualityAnalyzer.DescribeStats(facts.Ranges);
        var (retMin, retMax, retMedian, retMean, retStdDev) = RealMarketQualityAnalyzer.DescribeStats(consecutiveReturns);
        int zeroRangeBarsTotal = zeroRangeSegments.Sum(s => s.BarCount);

        var sb = new StringBuilder();
        sb.AppendLine("Metric,Value");
        sb.AppendLine($"TotalBars,{facts.TotalBars}");
        sb.AppendLine($"StructuralInvalidityCount,{facts.StructuralViolations}");
        sb.AppendLine($"NaNOrInfiniteCount,{facts.NaNOrInfiniteCount}");
        sb.AppendLine($"NonPositivePriceCount,{facts.NonPositivePriceCount}");
        sb.AppendLine($"NegativeVolumeCount,{facts.NegativeVolumeCount}");
        sb.AppendLine($"ZeroVolumeCount,{facts.ZeroVolumeCount}");
        sb.AppendLine($"IdenticalOhlcCount_ZeroRange,{facts.IdenticalOhlcCount}");
        sb.AppendLine($"ZeroRangeContiguousSegmentCount,{zeroRangeSegments.Count}");
        sb.AppendLine($"ZeroRangeBarsInLongestSegment,{(zeroRangeSegments.Count == 0 ? 0 : zeroRangeSegments.Max(s => s.BarCount))}");
        sb.AppendLine($"ZeroRangeBarsTotal,{zeroRangeBarsTotal}");
        sb.AppendLine($"ZeroRangeBarsPercentOfTotal,{(facts.TotalBars == 0 ? 0.0 : 100.0 * zeroRangeBarsTotal / facts.TotalBars).ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine("-- STRUCTURAL (invariant) vs STATISTICAL (extremeness) kept separate below --");
        sb.AppendLine($"Range_Min,{rangeMin.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Range_Max_StatisticalExtreme,{rangeMax.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Range_Median,{rangeMedian.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Range_Mean,{rangeMean.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"ConsecutiveReturn_Count,{consecutiveReturns.Count}");
        sb.AppendLine($"ConsecutiveReturn_Min,{retMin.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"ConsecutiveReturn_Max_StatisticalExtreme,{retMax.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"ConsecutiveReturn_Median,{retMedian.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"ConsecutiveReturn_Mean,{retMean.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"ConsecutiveReturn_StdDev,{retStdDev.ToString(CultureInfo.InvariantCulture)}");
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    public static void WriteDuplicateAnalysis(
        string path,
        int barsReceived,
        int barsWritten,
        int duplicates,
        int invalidRejected,
        int outOfOrder,
        IReadOnlyList<FormingBarSegment> zeroRangeSegments,
        string classificationNote)
    {
        double duplicateRate = barsReceived == 0 ? 0.0 : (double)duplicates / barsReceived;
        double avgAddAttemptsPerAcceptedBar = barsWritten == 0 ? 0.0 : (double)barsReceived / barsWritten;

        var sb = new StringBuilder();
        sb.AppendLine("Metric,Value");
        sb.AppendLine($"BarsReceived,{barsReceived}");
        sb.AppendLine($"BarsWritten,{barsWritten}");
        sb.AppendLine($"DuplicateRecordsRejected,{duplicates}");
        sb.AppendLine($"InvalidRejected,{invalidRejected}");
        sb.AppendLine($"OutOfOrder,{outOfOrder}");
        sb.AppendLine($"ArithmeticCheck_ReceivedEqualsWrittenPlusDuplicatesPlusInvalid,{barsReceived == barsWritten + duplicates + invalidRejected}");
        sb.AppendLine($"DuplicateRate,{duplicateRate.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"AverageAddAttemptsPerAcceptedBar,{avgAddAttemptsPerAcceptedBar.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"DedupIdentity,SessionId+Symbol+TimeFrame+CurrentBar (first Add() wins - ScientificDatasetCollector.cs)");
        sb.AppendLine($"ZeroRangeContiguousSegmentCount,{zeroRangeSegments.Count}");
        foreach (FormingBarSegment segment in zeroRangeSegments.OrderByDescending(s => s.BarCount).Take(10))
        {
            sb.AppendLine($"Segment_BarCount,{segment.BarCount}");
            sb.AppendLine($"Segment_StartIndex,{segment.StartIndex}");
            sb.AppendLine($"Segment_EndIndex,{segment.EndIndex}");
            sb.AppendLine($"Segment_StartTimestamp,{segment.StartTimestamp:O}");
            sb.AppendLine($"Segment_EndTimestamp,{segment.EndTimestamp:O}");
            sb.AppendLine($"Segment_MedianVolume,{segment.MedianVolumeInSegment.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Segment_MaxVolume,{segment.MaxVolumeInSegment.ToString(CultureInfo.InvariantCulture)}");
        }
        sb.AppendLine($"Classification,{Escape(classificationNote)}");
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    public static void WriteHorizonAnalysis(string path, HorizonAdequacyFacts wholeFile, HorizonAdequacyFacts? cleanSegmentOnly, IReadOnlyList<(int Start, int End)> runs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Metric,Value");
        sb.AppendLine($"Horizon,{wholeFile.Horizon}");
        sb.AppendLine($"RunCount,{wholeFile.RunCount}");
        sb.AppendLine($"LongestRunLength,{wholeFile.LongestRunLength}");
        sb.AppendLine($"TheoreticalWindows_WholeFile,{wholeFile.TheoreticalWindows}");
        sb.AppendLine($"UsableWindows_WholeFile,{wholeFile.UsableWindows}");
        sb.AppendLine($"BlockedByGaps_WholeFile,{wholeFile.BlockedByGaps}");
        sb.AppendLine($"PercentUsable_WholeFile,{wholeFile.PercentUsable.ToString(CultureInfo.InvariantCulture)}");
        if (cleanSegmentOnly is not null)
        {
            sb.AppendLine($"TheoreticalWindows_CleanSegmentOnly,{cleanSegmentOnly.TheoreticalWindows}");
            sb.AppendLine($"UsableWindows_CleanSegmentOnly,{cleanSegmentOnly.UsableWindows}");
            sb.AppendLine($"PercentUsable_CleanSegmentOnly,{cleanSegmentOnly.PercentUsable.ToString(CultureInfo.InvariantCulture)}");
        }
        sb.AppendLine("RunIndex,StartIndex,EndIndex,Length");
        for (int i = 0; i < runs.Count; i++)
            sb.AppendLine($"{i},{runs[i].Start},{runs[i].End},{runs[i].End - runs[i].Start + 1}");
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    public static void WriteSessionAnalysis(string path, IReadOnlyList<(string File, Guid SessionId, string Symbol, string TimeFrame, int BarsReceived, int BarsWritten, DateTime? First, DateTime? Last, string Notes)> sessions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SourceFile,SessionId,Symbol,TimeFrame,BarsReceived,BarsWritten,FirstTimestamp,LastTimestamp,Notes");
        foreach (var s in sessions)
        {
            sb.AppendLine(string.Join(",",
                Escape(s.File),
                s.SessionId.ToString("D"),
                Escape(s.Symbol),
                Escape(s.TimeFrame),
                s.BarsReceived.ToString(CultureInfo.InvariantCulture),
                s.BarsWritten.ToString(CultureInfo.InvariantCulture),
                s.First?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                s.Last?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                Escape(s.Notes)));
        }
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    public static void WriteAdmissibility(string path, string content) => File.WriteAllText(path, content, Utf8NoBom);

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
