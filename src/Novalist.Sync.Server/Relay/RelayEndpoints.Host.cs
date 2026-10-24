using System.Net.WebSockets;
using System.Text;

namespace Novalist.Sync.Server.Relay;

public static partial class RelayEndpoints
{
    private static async Task HostAsync(HttpContext context, RelaySessions sessions, RelayQuotas quotas)
    {
        if (!await CanUpgradeAsync(context)) return;
        var anonymous = !RelayAuthentication.Required(context);
        var proof = anonymous ? RelayAuthentication.HostProof(context) : null;
        if (anonymous && proof is null) { context.Response.StatusCode = 400; return; }
        var owner = RelayIdentity.From(context);
        if (!quotas.TryReserve(true, out var reservation, out var status, owner)) { Reject(context, status); return; }
        using var reserved = reservation;
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var session = sessions.Create(owner, proof);
        await using var lifetime = new RelayLifetime(context, session.Stop.Token);
        try
        {
            await SendIdAsync(socket, session.Id, lifetime.Token);
            var send = SendRequestsAsync(socket, session, lifetime.Token);
            var receive = socket.ReceiveAsync(new byte[1], lifetime.Token);
            await Task.WhenAny(send, receive);
            await lifetime.CancelAsync();
            await Task.WhenAll(send, receive);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (WebSocketException error) { ReportDisconnect(context, error); }
        finally
        {
            sessions.Remove(session);
            socket.Abort();
        }
    }

    private static async Task SendRequestsAsync(WebSocket socket, RelaySession session, CancellationToken ct)
    {
        await foreach (var id in session.Requests.Reader.ReadAllAsync(ct)) await SendIdAsync(socket, id, ct);
    }

    private static Task SendIdAsync(WebSocket socket, Guid id, CancellationToken ct) =>
        socket.SendAsync(Encoding.ASCII.GetBytes(id.ToString("N")), WebSocketMessageType.Text, true, ct);
}
