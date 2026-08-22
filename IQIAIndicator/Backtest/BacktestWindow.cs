using System;

namespace IQIAIndicator.Backtest;

/// <summary>
/// Sprint 15.25 (Lot 14.1, brief §9). An immutable, named time range. <see cref="Name"/> is free text -
/// "TRAIN"/"VALIDATION"/"OOS"/"HOLDOUT" are examples from the Lot 13 report (§14), never enforced
/// constants (brief §9: "NE PAS imposer ces noms comme constantes obligatoires").
///
/// Half-open convention: <see cref="From"/> is INCLUSIVE, <see cref="To"/> is EXCLUSIVE - matches
/// Core.MarketData.IHistoricalBarSource.Load's own convention, so two adjacent windows built with
/// `windowB.From == windowA.To` never share a bar and never leave a gap.
/// </summary>
public sealed class BacktestWindow
{
    public BacktestWindow(string name, DateTime from, DateTime to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (to <= from)
            throw new ArgumentException($"BacktestWindow '{name}': To ({to:O}) must be strictly after From ({from:O}) - To is exclusive.", nameof(to));

        Name = name;
        From = from;
        To = to;
    }

    public string Name { get; }

    public DateTime From { get; }

    public DateTime To { get; }

    /// <summary>True when <paramref name="timestamp"/> falls in [From, To).</summary>
    public bool Contains(DateTime timestamp) => timestamp >= From && timestamp < To;

    public override string ToString() => $"{Name} [{From:O} .. {To:O})";
}
