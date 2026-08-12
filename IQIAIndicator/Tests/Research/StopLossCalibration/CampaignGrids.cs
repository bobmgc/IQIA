using System;
using System.Collections.Generic;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>Sprint 15.10 (QDE-012 §10). Grids are protocol-locked - not tuned per-run.</summary>
public static class CampaignGrids
{
    public const int Horizon = 40;

    public static readonly IReadOnlyList<double> KGrid = BuildRange(0.25, 10.0, 0.25); // 40 points
    public static readonly IReadOnlyList<double> RSquaredGrid = BuildRange(0.00, 0.90, 0.05); // 19 points

    private static IReadOnlyList<double> BuildRange(double start, double end, double step)
    {
        var values = new List<double>();
        int n = (int)Math.Round((end - start) / step) + 1;
        for (int i = 0; i < n; i++)
        {
            values.Add(Math.Round(start + (i * step), 10));
        }

        return values;
    }
}
