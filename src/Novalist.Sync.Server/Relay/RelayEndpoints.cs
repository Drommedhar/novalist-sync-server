using System.Net.WebSockets;

namespace Novalist.Sync.Server.Relay;

public static partial class RelayEndpoints
{
    public static IEndpointRouteBuilder MapRelayEndpoints(this IEndpointRouteBuilder app)
    {
        var endpoints = app.MapGroup("/relay/v1");
        if (RelayAuthentication.Required(app.ServiceProvider.GetRequiredService<IConfiguration>())) endpoints.RequireAuthorization();
        else endpoints.AllowAnonymous();
        endpoints.MapGet("/host", HostAsync);
        endpoints.MapGet("/sessions/{sessionId:guid}/guest", GuestAsync);
        endpoints.MapGet("/sessions/{sessionId:guid}/routes/{routeId:guid}", AttachAsync);
        return app;
    }

    private static async Task<bool> CanUpgradeAsync(HttpContext context)
    {
        if (RelayAuthentication.Required(context) &&
            (string.IsNullOrEmpty(context.User.FindFirst("sub")?.Value) || string.IsNullOrEmpty(context.User.FindFirst("iss")?.Value)))
        { context.Response.StatusCode = 403; return false; }
        if (RelayAuthentication.Required(context) &&
            RelayLifetime.ExpiresAt(context) <= context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow())
        { context.Response.StatusCode = 401; return false; }
        if (!context.RequestServices.GetRequiredService<RelayQuotas>().Request(RelayIdentity.From(context), true))
        { context.Response.StatusCode = 429; context.Response.Headers.RetryAfter = "60"; return false; }
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return false; }
        if (!RelayAuthentication.Required(context)) return true;
        var state = await context.RequestServices.GetRequiredService<RelayTokenIntrospection>().CheckAsync(context, context.RequestAborted);
        if (state == RelayTokenState.Active &&
            RelayLifetime.ExpiresAt(context) > context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow()) return true;
        Reject(context, state == RelayTokenState.Unavailable ? 503 : 401);
        return false;
    }

    private static bool Reject(HttpContext context, int status)
    {
        if (status == 0) return false;
        context.Response.StatusCode = status;
        if (status == 429 || status == 503) context.Response.Headers.RetryAfter = "1";
        return true;
    }

    private static void ReportDisconnect(HttpContext context, WebSocketException error) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Novalist.Sync.Server.Relay")
            .LogDebug("Relay WebSocket disconnected: {ErrorCode}.", error.WebSocketErrorCode);
}
