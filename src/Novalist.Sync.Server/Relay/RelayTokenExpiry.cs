namespace Novalist.Sync.Server.Relay;

internal static class RelayTokenExpiry
{
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public static async Task WaitAsync(DateTimeOffset expiresAt, TimeProvider clock, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var remaining = expiresAt - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero) return;
            await Task.Delay(remaining < MaximumDelay ? remaining : MaximumDelay, clock, ct);
        }
    }
}
