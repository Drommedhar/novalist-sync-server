using System.Globalization;
using Microsoft.Extensions.Options;

namespace Novalist.Sync.Server.Relay;

internal sealed class RelayLifetime : IAsyncDisposable
{
    private readonly CancellationTokenSource stop;
    private readonly Task expiry;
    private readonly Task monitor;
    private int tokenState;
    public CancellationToken Token => stop.Token;
    public bool IsCancellationRequested => stop.IsCancellationRequested;
    public RelayTokenState TokenState => (RelayTokenState)Volatile.Read(ref tokenState);

    public RelayLifetime(HttpContext context, CancellationToken session = default, CancellationToken route = default)
    {
        var shutdown = context.RequestServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, session, route, shutdown);
        var authenticated = RelayAuthentication.Required(context);
        expiry = authenticated ? ExpireAsync(context) : Task.CompletedTask;
        monitor = authenticated ? MonitorAsync(context) : Task.CompletedTask;
    }

    public static DateTimeOffset ExpiresAt(HttpContext context) =>
        long.TryParse(context.User.FindFirst("exp")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) &&
        seconds is >= 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.MinValue;

    public Task CancelAsync() => stop.CancelAsync();

    private async Task ExpireAsync(HttpContext context)
    {
        try
        {
            await RelayTokenExpiry.WaitAsync(ExpiresAt(context), context.RequestServices.GetRequiredService<TimeProvider>(), stop.Token);
            await EndAsync(RelayTokenState.Inactive);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private Task EndAsync(RelayTokenState state)
    {
        Interlocked.CompareExchange(ref tokenState, (int)state, (int)RelayTokenState.Active);
        return stop.CancelAsync();
    }

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
                var state = await service.CheckAsync(context, stop.Token);
                if (state == RelayTokenState.Active) continue;
                await EndAsync(state);
                return;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { await stop.CancelAsync(); }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await Task.WhenAll(expiry, monitor);
        stop.Dispose();
    }
}
