using System.Globalization;
using Microsoft.Extensions.Options;

namespace Novalist.Sync.Server.Relay;

internal sealed class RelayLifetime : IAsyncDisposable
{
    private readonly CancellationTokenSource stop;
    private readonly CancellationTokenSource? expiry;
    private readonly Task monitor;
    public CancellationToken Token => stop.Token;
    public bool IsCancellationRequested => stop.IsCancellationRequested;

    public RelayLifetime(HttpContext context, CancellationToken session = default, CancellationToken route = default)
    {
        if (RelayAuthentication.Required(context))
        {
            var clock = context.RequestServices.GetRequiredService<TimeProvider>();
            var remaining = ExpiresAt(context) - clock.GetUtcNow();
            expiry = new(TimeSpan.FromMilliseconds(Math.Clamp(remaining.TotalMilliseconds, 0, uint.MaxValue - 1)), clock);
        }
        stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, session, route, expiry?.Token ?? default);
        monitor = expiry is null ? Task.CompletedTask : MonitorAsync(context);
    }

    public static DateTimeOffset ExpiresAt(HttpContext context) =>
        long.TryParse(context.User.FindFirst("exp")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) &&
        seconds is >= 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.MinValue;

    public Task CancelAsync() => stop.CancelAsync();

    private async Task MonitorAsync(HttpContext context)
    {
        var service = context.RequestServices.GetRequiredService<RelayTokenIntrospection>();
        var interval = context.RequestServices.GetRequiredService<IOptions<RelayIntrospectionOptions>>().Value.CheckIntervalSeconds;
        var clock = context.RequestServices.GetRequiredService<TimeProvider>();
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), clock, stop.Token);
                if (await service.CheckAsync(context, stop.Token) == RelayTokenState.Active) continue;
                await stop.CancelAsync();
                return;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { await stop.CancelAsync(); }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await monitor;
        stop.Dispose();
        expiry?.Dispose();
    }
}
