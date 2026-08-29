using System;
using System.Collections.Generic;
using System.Threading;
using IQIAIndicator.Core.MarketData.Yahoo;

namespace IQIAIndicator.Tests.BacktestTests.Yahoo;

/// <summary>Sprint 15.25 (Lot 14.2). Deterministic, network-free stand-in for
/// <see cref="IYahooChartClient"/> - returns pre-configured JSON in call order and records every
/// invocation's parameters so tests can assert on chunking/mapping without any HTTP call.
///
/// Sprint 15.25 (Lot 15.25-XX): honours the new <see cref="CancellationToken"/> (throws
/// <see cref="OperationCanceledException"/> if already cancelled) and can be told to throw a
/// <see cref="YahooProviderException"/> instead of returning a body, so source-level classification /
/// cancellation can be exercised without HTTP.</summary>
internal sealed class FakeYahooChartClient : IYahooChartClient
{
    public sealed record Call(string Ticker, string Interval, DateTime PeriodStartUtc, DateTime PeriodEndUtc);

    private readonly Queue<string> _responses;

    public List<Call> Calls { get; } = new();

    /// <summary>When set, every call throws this instead of dequeuing a response.</summary>
    public YahooProviderException? ThrowOnEveryCall { get; set; }

    public FakeYahooChartClient(params string[] responsesInCallOrder)
    {
        _responses = new Queue<string>(responsesInCallOrder);
    }

    public string FetchChartJson(
        string yahooTicker,
        string yahooInterval,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(new Call(yahooTicker, yahooInterval, periodStartUtc, periodEndUtc));

        if (ThrowOnEveryCall is not null)
            throw ThrowOnEveryCall;

        if (_responses.Count == 0)
            throw new InvalidOperationException("FakeYahooChartClient has no more configured responses for this call.");

        return _responses.Dequeue();
    }
}
