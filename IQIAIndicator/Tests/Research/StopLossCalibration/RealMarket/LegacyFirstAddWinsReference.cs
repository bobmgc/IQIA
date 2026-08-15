using System.Collections.Generic;
using IQIAIndicator.Core.Calibration;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.RealMarket;

/// <summary>
/// Sprint 15.19 (QDE-012 forming-bar-capture fix) - comparison-only reference reimplementation of
/// ScientificDatasetCollector's PRE-15.19 dedup rule ("first Add() wins": the first record seen for a
/// given (Symbol, TimeFrame, CurrentBar) is kept forever; every later call for the same identity is
/// discarded). This class does not exist to change any production behavior - the real pre-15.19
/// behavior is exactly what shipped before this sprint (see git history / QDE-012_Sprint_15.17/15.18
/// reports) - it exists solely so Sprint1519ComparisonTests can show, side by side and on the exact
/// same input sequence, what the OLD collector would have committed versus what the NEW
/// ScientificDatasetCollector (Core/Calibration/ScientificDatasetCollector.cs) actually commits.
/// Deliberately minimal: no OHLCV structural validation, no diagnostics - just the one rule being
/// compared.
/// </summary>
public static class LegacyFirstAddWinsReference
{
    public static IReadOnlyList<ScientificDatasetRecord> Simulate(IReadOnlyList<ScientificDatasetRecord> callbacks)
    {
        var seen = new HashSet<(string Symbol, string TimeFrame, int CurrentBar)>();
        var kept = new List<ScientificDatasetRecord>();

        foreach (ScientificDatasetRecord record in callbacks)
        {
            var identity = (record.Symbol, record.TimeFrame, record.CurrentBar);
            if (seen.Add(identity))
                kept.Add(record); // first occurrence only - every later callback for this identity is discarded
        }

        return kept;
    }
}
