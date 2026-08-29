using System;
using IQIAIndicator.Core.MarketData.Yahoo;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 15.25-XX). Pure tests of the bounded <see cref="YahooRetryPolicy"/> decision
/// function - no HTTP, no time. These pin the two hard ceilings (retry count, cumulative wait) and the
/// "Retry-After never wins over the application budget" rule.
/// </summary>
public sealed class YahooRetryPolicyTests
{
    private static YahooRetryPolicy Policy(
        int maxRetries = 3,
        double maxTotalWaitSeconds = 20,
        double baseDelaySeconds = 1,
        double maxDelayPerWaitSeconds = 8)
        => new(
            maximumRetryCount: maxRetries,
            maximumTotalWait: TimeSpan.FromSeconds(maxTotalWaitSeconds),
            perRequestTimeout: TimeSpan.FromSeconds(15),
            baseDelay: TimeSpan.FromSeconds(baseDelaySeconds),
            maximumDelayPerWait: TimeSpan.FromSeconds(maxDelayPerWaitSeconds));

    [Fact]
    public void NonRetryableKind_AlwaysStops()
    {
        YahooRetryDecision decision = Policy().Decide(1, YahooFailureKind.InvalidResponse, retryAfter: null, alreadyWaited: TimeSpan.Zero);
        Assert.False(decision.ShouldRetry);
    }

    [Theory]
    [InlineData(YahooFailureKind.RateLimited)]
    [InlineData(YahooFailureKind.ProviderUnavailable)]
    [InlineData(YahooFailureKind.NetworkFailure)]
    public void RetryableKind_RetriesUpToMaxCount_ThenStops(YahooFailureKind kind)
    {
        YahooRetryPolicy policy = Policy(maxRetries: 3);

        Assert.True(policy.Decide(1, kind, null, TimeSpan.Zero).ShouldRetry);
        Assert.True(policy.Decide(2, kind, null, TimeSpan.Zero).ShouldRetry);
        Assert.True(policy.Decide(3, kind, null, TimeSpan.Zero).ShouldRetry);
        Assert.False(policy.Decide(4, kind, null, TimeSpan.Zero).ShouldRetry); // 4th failed attempt = no more retries
    }

    [Fact]
    public void Backoff_IsExponential_AndCappedPerWait()
    {
        YahooRetryPolicy policy = Policy(maxRetries: 10, baseDelaySeconds: 1, maxDelayPerWaitSeconds: 8, maxTotalWaitSeconds: 999);

        Assert.Equal(TimeSpan.FromSeconds(1), policy.Decide(1, YahooFailureKind.RateLimited, null, TimeSpan.Zero).Delay);
        Assert.Equal(TimeSpan.FromSeconds(2), policy.Decide(2, YahooFailureKind.RateLimited, null, TimeSpan.Zero).Delay);
        Assert.Equal(TimeSpan.FromSeconds(4), policy.Decide(3, YahooFailureKind.RateLimited, null, TimeSpan.Zero).Delay);
        Assert.Equal(TimeSpan.FromSeconds(8), policy.Decide(4, YahooFailureKind.RateLimited, null, TimeSpan.Zero).Delay); // capped, not 16
        Assert.Equal(TimeSpan.FromSeconds(8), policy.Decide(5, YahooFailureKind.RateLimited, null, TimeSpan.Zero).Delay); // stays capped
    }

    [Fact]
    public void RetryAfter_LongerThanBackoff_IsRespected_ButClampedToRemainingBudget()
    {
        YahooRetryPolicy policy = Policy(maxTotalWaitSeconds: 20);

        // Provider asks for 10 minutes; budget is 20 s and nothing spent yet -> we sleep at most 20 s.
        YahooRetryDecision decision = policy.Decide(
            attemptsMade: 1,
            kind: YahooFailureKind.RateLimited,
            retryAfter: TimeSpan.FromMinutes(10),
            alreadyWaited: TimeSpan.Zero);

        Assert.True(decision.ShouldRetry);
        Assert.Equal(TimeSpan.FromSeconds(20), decision.Delay);
        Assert.True(decision.Delay <= policy.MaximumTotalWait);
    }

    [Fact]
    public void WhenCumulativeBudgetIsSpent_Stops()
    {
        YahooRetryPolicy policy = Policy(maxTotalWaitSeconds: 20);

        YahooRetryDecision decision = policy.Decide(
            attemptsMade: 2,
            kind: YahooFailureKind.RateLimited,
            retryAfter: null,
            alreadyWaited: TimeSpan.FromSeconds(20));

        Assert.False(decision.ShouldRetry);
    }

    [Fact]
    public void CumulativeSleep_CanNeverExceed_MaximumTotalWait()
    {
        YahooRetryPolicy policy = Policy(maxRetries: 50, maxTotalWaitSeconds: 20, baseDelaySeconds: 3, maxDelayPerWaitSeconds: 30);

        TimeSpan waited = TimeSpan.Zero;
        for (int attempt = 1; attempt <= 50; attempt++)
        {
            YahooRetryDecision decision = policy.Decide(attempt, YahooFailureKind.RateLimited, retryAfter: TimeSpan.FromHours(1), alreadyWaited: waited);
            if (!decision.ShouldRetry)
                break;
            waited += decision.Delay;
            Assert.True(waited <= policy.MaximumTotalWait, $"cumulative wait {waited} exceeded budget {policy.MaximumTotalWait}");
        }

        Assert.True(waited <= policy.MaximumTotalWait);
    }
}
