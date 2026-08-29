using System;

namespace IQIAIndicator.Core.MarketData.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 15.25-XX). Strictly bounded retry policy for <see cref="HttpYahooChartClient"/>.
///
/// Two hard ceilings, both of which must hold at all times:
/// <list type="bullet">
///   <item><see cref="MaximumRetryCount"/> - the number of RETRIES after the initial attempt (so at
///     most <c>1 + MaximumRetryCount</c> HTTP attempts per chunk).</item>
///   <item><see cref="MaximumTotalWait"/> - the cumulative time the client may spend SLEEPING between
///     attempts. A <c>Retry-After</c> header is read and respected, but is clamped so it can never
///     push cumulative sleeping past this budget (brief Phase 3: "Bounded application behavior always
///     wins").</item>
/// </list>
/// <see cref="PerRequestTimeout"/> bounds each individual HTTP attempt and REPLACES the old
/// <c>HttpClient.Timeout = 30s</c> (a single timeout system, not two overlapping ones).
///
/// All four numbers are <c>Provisional</c> in the same sense as <c>FusionStateManager</c>'s
/// <c>alpha</c>/<c>HysteresisThreshold</c>: chosen to make the suite terminate deterministically and
/// quickly, never calibrated against a scientific objective. Worst-case wall-clock for one chunk is
/// <c>(1 + MaximumRetryCount) * PerRequestTimeout + MaximumTotalWait</c> - with the defaults, ≈ 80 s.
/// </summary>
public sealed class YahooRetryPolicy
{
    public YahooRetryPolicy(
        int maximumRetryCount,
        TimeSpan maximumTotalWait,
        TimeSpan perRequestTimeout,
        TimeSpan baseDelay,
        TimeSpan maximumDelayPerWait)
    {
        if (maximumRetryCount < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRetryCount), maximumRetryCount, "must be >= 0.");
        if (maximumTotalWait < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumTotalWait), maximumTotalWait, "must be >= 0.");
        if (perRequestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(perRequestTimeout), perRequestTimeout, "must be > 0.");
        if (baseDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(baseDelay), baseDelay, "must be >= 0.");
        if (maximumDelayPerWait < baseDelay)
            throw new ArgumentOutOfRangeException(nameof(maximumDelayPerWait), maximumDelayPerWait, "must be >= baseDelay.");

        MaximumRetryCount = maximumRetryCount;
        MaximumTotalWait = maximumTotalWait;
        PerRequestTimeout = perRequestTimeout;
        BaseDelay = baseDelay;
        MaximumDelayPerWait = maximumDelayPerWait;
    }

    /// <summary>Retries after the first attempt. Defaults to 3 (≤ 4 HTTP attempts per chunk).</summary>
    public int MaximumRetryCount { get; }

    /// <summary>Hard cap on cumulative sleeping between attempts. Defaults to 20 s.</summary>
    public TimeSpan MaximumTotalWait { get; }

    /// <summary>Hard cap on a single HTTP attempt. Defaults to 15 s.</summary>
    public TimeSpan PerRequestTimeout { get; }

    /// <summary>First backoff step; doubles each retry. Defaults to 1 s (1 → 2 → 4 …).</summary>
    public TimeSpan BaseDelay { get; }

    /// <summary>Cap on any single backoff sleep. Defaults to 8 s.</summary>
    public TimeSpan MaximumDelayPerWait { get; }

    /// <summary>Production defaults. Worst-case ≈ 80 s per chunk; typically the very first retry
    /// succeeds or the whole thing fails fast with a classified <see cref="YahooProviderException"/>.</summary>
    public static YahooRetryPolicy Default { get; } = new(
        maximumRetryCount: 3,
        maximumTotalWait: TimeSpan.FromSeconds(20),
        perRequestTimeout: TimeSpan.FromSeconds(15),
        baseDelay: TimeSpan.FromSeconds(1),
        maximumDelayPerWait: TimeSpan.FromSeconds(8));

    /// <summary>
    /// Pure decision for the attempt that just failed. <paramref name="attemptsMade"/> is 1 after the
    /// initial attempt fails, 2 after the first retry fails, and so on. <paramref name="alreadyWaited"/>
    /// is the cumulative sleep spent so far. Returns whether to retry and, if so, exactly how long to
    /// sleep first - always &gt; <see cref="TimeSpan.Zero"/> and always within the remaining budget.
    /// </summary>
    public YahooRetryDecision Decide(
        int attemptsMade,
        YahooFailureKind kind,
        TimeSpan? retryAfter,
        TimeSpan alreadyWaited)
    {
        if (attemptsMade < 1)
            throw new ArgumentOutOfRangeException(nameof(attemptsMade), attemptsMade, "must be >= 1.");

        bool retryableKind = kind is YahooFailureKind.RateLimited
            or YahooFailureKind.ProviderUnavailable
            or YahooFailureKind.NetworkFailure;
        if (!retryableKind)
            return YahooRetryDecision.Stop;

        if (attemptsMade > MaximumRetryCount)
            return YahooRetryDecision.Stop;

        TimeSpan remaining = MaximumTotalWait - alreadyWaited;
        if (remaining <= TimeSpan.Zero)
            return YahooRetryDecision.Stop;

        // Exponential backoff: BaseDelay * 2^(attemptsMade-1), capped per-wait.
        double factor = Math.Pow(2, attemptsMade - 1);
        TimeSpan backoff = TimeSpan.FromTicks((long)Math.Min(BaseDelay.Ticks * factor, MaximumDelayPerWait.Ticks));

        // Respect Retry-After when the provider sent one and it is longer than our own backoff...
        TimeSpan wanted = backoff;
        if (retryAfter is { } ra && ra > wanted)
            wanted = ra;

        // ...but never let it (or the backoff) exceed the remaining hard budget.
        TimeSpan delay = wanted <= remaining ? wanted : remaining;
        if (delay <= TimeSpan.Zero)
            return YahooRetryDecision.Stop;

        return YahooRetryDecision.RetryAfter(delay);
    }
}

/// <summary>Outcome of <see cref="YahooRetryPolicy.Decide"/>.</summary>
public readonly record struct YahooRetryDecision(bool ShouldRetry, TimeSpan Delay)
{
    public static YahooRetryDecision Stop { get; } = new(false, TimeSpan.Zero);

    public static YahooRetryDecision RetryAfter(TimeSpan delay) => new(true, delay);
}
