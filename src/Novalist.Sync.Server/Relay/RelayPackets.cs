using System.Net.WebSockets;

namespace Novalist.Sync.Server.Relay;

internal static class RelayPackets
{
    public static async Task TransferAsync(WebSocket guest, WebSocket host, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var forward = CopyAsync(guest, host, lifetime.Token);
        var backward = CopyAsync(host, guest, lifetime.Token);
        try
        {
            var finished = await Task.WhenAny(forward, backward);
            await finished;
            lifetime.CancelAfter(TimeSpan.FromSeconds(5));
            await Task.WhenAll(forward, backward);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            await lifetime.CancelAsync();
            if (guest.State != WebSocketState.Closed) guest.Abort();
            if (host.State != WebSocketState.Closed) host.Abort();
            try { await Task.WhenAll(forward, backward); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (WebSocketException) when (lifetime.IsCancellationRequested) { }
        }
    }

    private static async Task CopyAsync(WebSocket source, WebSocket destination, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var message = await source.ReceiveAsync(buffer.AsMemory(), ct);
            if (message.MessageType == WebSocketMessageType.Close)
            {
                await destination.CloseOutputAsync(source.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                    source.CloseStatusDescription, ct);
                return;
            }
            if (message.MessageType != WebSocketMessageType.Binary) throw new WebSocketException("Expected an opaque binary relay packet.");
            await destination.SendAsync(buffer.AsMemory(0, message.Count), WebSocketMessageType.Binary, message.EndOfMessage, ct);
        }
    }
}
