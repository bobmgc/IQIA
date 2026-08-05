namespace IQIAIndicator.Core;

/// <summary>
/// Prix OHLC bruts et dérivés stateless calculés depuis le bar (médiane, prix typique).
/// ATR et VWAP seront fournis par MarketCache au Sprint 2.
/// </summary>
public readonly record struct PriceInfo(
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Median,
    decimal TypicalPrice
);
