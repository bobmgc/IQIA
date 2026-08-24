using System;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §9). Declares WHICH dataset an experiment intends to use - provider,
/// symbol, timeframe, and the calendar range - without carrying the bars themselves (see
/// <see cref="CalibrationDataset"/> for the pairing with an actual, already-loaded
/// <see cref="Core.MarketData.HistoricalSeries"/>). Two specifications with the same field values describe
/// the SAME intended dataset; whether the underlying series is byte-identical is what
/// <see cref="CalibrationDataset.Fingerprint"/> (brief §10, reusing <see cref="HistoricalSeriesFingerprint"/>)
/// actually proves.
/// </summary>
public sealed record CalibrationDatasetSpecification
{
    public required string Provider { get; init; }

    public required string Symbol { get; init; }

    public required string Timeframe { get; init; }

    /// <summary>Inclusive start of the intended range - same half-open convention as
    /// <see cref="BacktestWindow"/>: <see cref="End"/> is exclusive.</summary>
    public required DateTime Start { get; init; }

    public required DateTime End { get; init; }

    public static CalibrationDatasetSpecification Create(string provider, string symbol, string timeframe, DateTime start, DateTime end)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeframe);

        if (end <= start)
            throw new ArgumentException($"End ({end:O}) must be strictly after Start ({start:O}).");

        return new CalibrationDatasetSpecification { Provider = provider, Symbol = symbol, Timeframe = timeframe, Start = start, End = end };
    }
}
