using System.Net.WebSockets;
using System.Text;

namespace Novalist.Sync.Server.Relay;

public static class RelayEndpoints
{
    public static IEndpointRouteBuilder MapRelayEndpoints(this IEndpointRouteBuilder app)
    {
        var endpoints = app.MapGroup("/relay/v1").RequireAuthorization();
        endpoints.MapGet("/host", HostAsync);
        endpoints.MapGet("/sessions/{sessionId:guid}/guest", GuestAsync);
        endpoints.MapGet("/sessions/{sessionId:guid}/routes/{routeId:guid}", AttachAsync);
        return app;
    }

    private static async Task HostAsync(HttpContext context, RelaySessions sessions)
    {
        if (!CanUpgrade(context)) return;
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var session = sessions.Create(context.User.FindFirst("iss")!.Value, context.User.FindFirst("sub")!.Value);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, session.Stop.Token);
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
        catch (WebSocketException) { }
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

    private static async Task GuestAsync(HttpContext context, Guid sessionId, RelaySessions sessions)
    {
        if (!CanUpgrade(context)) return;
        var session = sessions.Find(sessionId);
        if (session is null || session.Stop.IsCancellationRequested) { context.Response.StatusCode = 404; return; }
        var routeId = Guid.NewGuid();
        var route = new RelayRoute();
        session.Routes[routeId] = route;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, session.Stop.Token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            if (!session.Requests.Writer.TryWrite(routeId)) { context.Response.StatusCode = 503; return; }
            var host = await route.Host.Task.WaitAsync(deadline.Token);
            using var guest = await context.WebSockets.AcceptWebSocketAsync();
            await RelayPackets.TransferAsync(guest, host, lifetime.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = 410;
        }
        catch (WebSocketException) { }
        finally
        {
            session.Routes.TryRemove(routeId, out _);
            route.Done.TrySetResult();
        }
    }

    private static async Task AttachAsync(HttpContext context, Guid sessionId, Guid routeId, RelaySessions sessions)
    {
        if (!CanUpgrade(context)) return;
        var session = sessions.Find(sessionId);
        if (session is null || session.Stop.IsCancellationRequested) { context.Response.StatusCode = 404; return; }
        if (session.Subject != context.User.FindFirst("sub")!.Value || session.Issuer != context.User.FindFirst("iss")!.Value)
        { context.Response.StatusCode = 403; return; }
        if (!session.Routes.TryGetValue(routeId, out var route) || route.Host.Task.IsCompleted)
        { context.Response.StatusCode = 404; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        if (!route.Host.TrySetResult(socket)) { socket.Abort(); return; }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, session.Stop.Token);
        try { await route.Done.Task.WaitAsync(lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { socket.Abort(); }
    }

    private static bool CanUpgrade(HttpContext context)
    {
        if (string.IsNullOrEmpty(context.User.FindFirst("sub")?.Value) || string.IsNullOrEmpty(context.User.FindFirst("iss")?.Value))
        { context.Response.StatusCode = 403; return false; }
        if (context.WebSockets.IsWebSocketRequest) return true;
        context.Response.StatusCode = 400;
        return false;
    }
}
