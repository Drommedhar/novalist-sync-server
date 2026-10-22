using System.Net.WebSockets;

namespace Novalist.Sync.Server.Relay;

internal static class RelayPackets
{
    public static async Task TransferAsync(WebSocket guest, WebSocket host, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var forward = CopyAsync(guest, host, lifetime.Token);
        var backward = CopyAsync(host, guest, lifetime.Token);
        try { await Task.WhenAny(forward, backward); }
        finally
        {
            await lifetime.CancelAsync();
            guest.Abort();
            host.Abort();
        }
        try { await Task.WhenAll(forward, backward); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private static async Task CopyAsync(WebSocket source, WebSocket destination, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var message = await source.ReceiveAsync(buffer.AsMemory(), ct);
            if (message.MessageType == WebSocketMessageType.Close) return;
            if (message.MessageType != WebSocketMessageType.Binary) throw new WebSocketException("Expected an opaque binary relay packet.");
            await destination.SendAsync(buffer.AsMemory(0, message.Count), WebSocketMessageType.Binary, message.EndOfMessage, ct);
        }
    }
}
