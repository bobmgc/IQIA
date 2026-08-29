using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IQIAIndicator.Tests.BacktestTests.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 15.25-XX). Scripted <see cref="HttpMessageHandler"/> for the Yahoo resilience
/// tests - each call dequeues the next step. No socket, no network. A step can return a response
/// (any status code / body / <c>Retry-After</c>), throw a transport exception, or stall until the
/// caller's per-request timeout cancels it (exercising the REAL <see cref="CancellationTokenSource"/>
/// path in <c>HttpYahooChartClient</c>, just with a millisecond budget).
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _steps;

    public StubHttpMessageHandler(params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] steps)
    {
        _steps = new Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(steps);
    }

    public int CallCount { get; private set; }

    public List<string> RequestedUrls { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        RequestedUrls.Add(request.RequestUri!.ToString());

        if (_steps.Count == 0)
            throw new InvalidOperationException($"StubHttpMessageHandler ran out of scripted steps (call #{CallCount}).");

        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> step = _steps.Dequeue();
        return await step(request, cancellationToken).ConfigureAwait(false);
    }

    // ── Step factories ────────────────────────────────────────────────────────────────────────────

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond(
        HttpStatusCode status,
        string body = "{}",
        string contentType = "application/json",
        string? retryAfter = null)
        => (_, _) =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType),
            };
            if (retryAfter is not null)
                response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
            return Task.FromResult(response);
        };

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Throw(Exception exception)
        => (_, _) => throw exception;

    /// <summary>Never completes until the per-request timeout cancels it.</summary>
    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Stall()
        => async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK); // unreachable
        };
}
