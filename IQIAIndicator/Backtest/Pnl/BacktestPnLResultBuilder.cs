using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Backtest.Pnl;

/// <summary>
/// Sprint 15.25 (Lot 14.6, brief §10/§16/§17/§20). Builds the equity curve, drawdown series and
/// ALL/BUY/SELL summaries from a population of <see cref="PositionPnLResult"/>. Stateless (static class,
/// no fields) - the same isolation guarantee as every prior lot's engine (brief §33).
///
/// "EQUITY[0] = STARTINGCAPITAL" WITHOUT A FABRICATED POINT (brief §11's own worked example): the running
/// peak used for <see cref="EquityPoint.Drawdown"/> starts at 0 (the implicit CumulativePnL baseline
/// BEFORE any position), which is mathematically equivalent to seeding a "point zero" at
/// Equity=StartingCapital, since Drawdown is a DIFFERENCE and a constant StartingCapital offset cancels
/// out of it. Verified bit-for-bit against every worked example in the brief (§17: sequence
/// 100000/101000/99000/98000/103000 -&gt; MaximumDrawdown=-3000; §37: win-then-loss -&gt; -300; §38:
/// loss-then-win -&gt; -500) without ever constructing an <see cref="EquityPoint"/> with an invented
/// timestamp - a real position's real ExitTimestamp backs every single point.
/// </summary>
public static class BacktestPnLResultBuilder
{
    public static BacktestPnLResult Build(IReadOnlyList<PositionPnLResult> positionResults, decimal? startingCapital)
    {
        ArgumentNullException.ThrowIfNull(positionResults);

        if (startingCapital is decimal capital && capital <= 0m)
            throw new ArgumentOutOfRangeException(nameof(startingCapital), capital, "StartingCapital, when supplied, must be strictly positive.");

        // Brief §13: deterministic total order - ExitTimestamp, then PositionId as the tie-break. Never
        // the incidental order of a List/Dictionary.
        List<PositionPnLResult> closed = positionResults
            .Where(p => p.Status == PositionStatus.Closed)
            .OrderBy(p => p.ExitTimestamp!.Value)
            .ThenBy(p => p.PositionId)
            .ToList();

        var equityCurve = new List<EquityPoint>(closed.Count);
        decimal cumulative = 0m;
        decimal peak = 0m; // implicit "Equity[0]" baseline - see class doc comment.

        foreach (PositionPnLResult position in closed)
        {
            decimal incremental = position.GrossPnL!.Value;
            cumulative += incremental;
            if (cumulative > peak)
                peak = cumulative;

            decimal drawdown = cumulative - peak;
            decimal? equity = startingCapital is decimal sc ? sc + cumulative : null;
            double? drawdownPercent = startingCapital is decimal sc2
                ? (double)(drawdown / (sc2 + peak))
                : null;

            equityCurve.Add(new EquityPoint(
                position.ExitTimestamp!.Value, position.PositionId, incremental, cumulative, equity, drawdown, drawdownPercent));
        }

        decimal finalGrossPnL = equityCurve.Count > 0 ? equityCurve[^1].CumulativePnL : 0m;
        decimal maximumDrawdown = equityCurve.Count > 0 ? equityCurve.Min(point => point.Drawdown) : 0m;
        decimal? finalEquity = startingCapital is decimal startingForFinal ? startingForFinal + finalGrossPnL : null;

        PnLSummary all = Summarize(positionResults);
        PnLSummary buy = Summarize(positionResults.Where(p => p.Direction == DirectionCandidate.BUY_CANDIDATE).ToList());
        PnLSummary sell = Summarize(positionResults.Where(p => p.Direction == DirectionCandidate.SELL_CANDIDATE).ToList());

        return new BacktestPnLResult(
            positionResults, all, buy, sell, equityCurve.AsReadOnly(),
            finalGrossPnL, maximumDrawdown, startingCapital, finalEquity,
            PnLFingerprint.ComputeHash(equityCurve));
    }

    private static PnLSummary Summarize(IReadOnlyList<PositionPnLResult> positions)
    {
        List<PositionPnLResult> closed = positions.Where(p => p.Status == PositionStatus.Closed).ToList();

        // Brief §21: strictly > / < - a break-even (GrossPnL == 0) position belongs to neither bucket.
        decimal grossProfit = closed.Where(p => p.GrossPnL!.Value > 0m).Sum(p => p.GrossPnL!.Value);
        decimal grossLoss = closed.Where(p => p.GrossPnL!.Value < 0m).Sum(p => p.GrossPnL!.Value);
        decimal netGrossPnL = grossProfit + grossLoss;
        int winning = closed.Count(p => p.GrossPnL!.Value > 0m);
        int losing = closed.Count(p => p.GrossPnL!.Value < 0m);

        double? winRate = closed.Count > 0 ? (double)winning / closed.Count : null;
        decimal? average = closed.Count > 0 ? netGrossPnL / closed.Count : null;
        decimal? median = Median(closed.Select(p => p.GrossPnL!.Value).ToList());

        return new PnLSummary(positions.Count, closed.Count, grossProfit, grossLoss, netGrossPnL, winning, losing, winRate, average, median);
    }

    /// <summary>
    /// Sprint 15.25 (Lot 14.6, brief §24): exact, documented median convention - a decimal-native
    /// reimplementation of Lot 14.4's <c>ScientificMeasurementSummaryBuilder.Median</c> convention
    /// (sort ascending; odd count -&gt; middle element; even count -&gt; average of the two middle
    /// elements; empty -&gt; null, never 0). Kept decimal-native rather than routing through the existing
    /// double-based helper, to avoid a decimal-&gt;double-&gt;decimal round trip on money values - the SAME
    /// convention, not a competing one.
    /// </summary>
    public static decimal? Median(IReadOnlyList<decimal> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
            return null;

        decimal[] sorted = values.OrderBy(value => value).ToArray();
        int n = sorted.Length;

        return n % 2 == 1
            ? sorted[n / 2]
            : (sorted[n / 2 - 1] + sorted[n / 2]) / 2m;
    }
}
