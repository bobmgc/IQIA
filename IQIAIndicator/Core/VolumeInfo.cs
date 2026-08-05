namespace IQIAIndicator.Core;

/// <summary>
/// Volume brut et flux bid/ask lus directement depuis l'API ATAS.
/// CumulativeDelta et AverageVolume seront fournis par MarketCache au Sprint 2.
/// </summary>
public readonly record struct VolumeInfo(
    decimal Volume,
    decimal BidVolume,
    decimal AskVolume,
    decimal Delta
);
