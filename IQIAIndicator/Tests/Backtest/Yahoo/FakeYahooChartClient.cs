using System;
using System.Collections.Generic;
using IQIAIndicator.Core.MarketData.Yahoo;

namespace IQIAIndicator.Tests.BacktestTests.Yahoo;

/// <summary>Sprint 15.25 (Lot 14.2). Deterministic, network-free stand-in for
/// <see cref="IYahooChartClient"/> - returns pre-configured JSON in call order and records every
/// invocation's parameters so tests can assert on chunking/mapping without any HTTP call.</summary>
internal sealed class FakeYahooChartClient : IYahooChartClient
{
    public sealed record Call(string Ticker, string Interval, DateTime PeriodStartUtc, DateTime PeriodEndUtc);

    private readonly Queue<string> _responses;

    public List<Call> Calls { get; } = new();

    public FakeYahooChartClient(params string[] responsesInCallOrder)
    {
        _responses = new Queue<string>(responsesInCallOrder);
    }

    public string FetchChartJson(string yahooTicker, string yahooInterval, DateTime periodStartUtc, DateTime periodEndUtc)
    {
        Calls.Add(new Call(yahooTicker, yahooInterval, periodStartUtc, periodEndUtc));

        if (_responses.Count == 0)
            throw new InvalidOperationException("FakeYahooChartClient has no more configured responses for this call.");

        return _responses.Dequeue();
    }
}
