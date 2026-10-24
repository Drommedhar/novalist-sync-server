using Novalist.Sync.Server.Relay;

namespace Novalist.Sync.Server.Tests.Relay;

public sealed class RelayTokenExpiryTests
{
    [Fact]
    public async Task LongLivedTokensSurviveTheTimerLimitAndExpireAtTheirActualDeadline()
    {
        var clock = new RelayTestClock();
        var expiresAt = clock.GetUtcNow().AddDays(90);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var expired = RelayTokenExpiry.WaitAsync(expiresAt, clock, cancellation.Token);
        var first = await clock.NextDelayAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(uint.MaxValue - 1), first);
        clock.Advance(TimeSpan.FromDays(50));
        Assert.Equal(TimeSpan.FromDays(40), await clock.NextDelayAsync());
        Assert.False(expired.IsCompleted);
        clock.Advance(TimeSpan.FromDays(39));
        Assert.False(expired.IsCompleted);
        clock.Advance(TimeSpan.FromDays(1));
        await expired.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token);
    }

    [Fact]
    public async Task ClosingAConnectionCancelsItsLongExpiryWait()
    {
        var clock = new RelayTestClock();
        using var cancellation = new CancellationTokenSource();
        var expired = RelayTokenExpiry.WaitAsync(clock.GetUtcNow().AddDays(90), clock, cancellation.Token);
        await clock.NextDelayAsync();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => expired);
    }
}
