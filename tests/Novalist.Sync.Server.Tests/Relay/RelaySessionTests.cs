using System.Net;
using System.Net.WebSockets;

namespace Novalist.Sync.Server.Tests.Relay;

public sealed class RelaySessionTests
{
    [Fact]
    public async Task AuthenticatedEndpointsRejectMissingTokensAndUnknownSessions()
    {
        await using var server = new RelayTestServer();
        await server.RejectedAsync("host", HttpStatusCode.Unauthorized, null);
        await server.RejectedAsync($"sessions/{Guid.NewGuid()}/guest", HttpStatusCode.Unauthorized, null);
        await server.RejectedAsync($"sessions/{Guid.NewGuid()}/routes/{Guid.NewGuid()}", HttpStatusCode.Unauthorized, null);
        await server.RejectedAsync($"sessions/{Guid.NewGuid()}/guest", HttpStatusCode.NotFound);
        await server.RejectedAsync($"sessions/{Guid.NewGuid()}/routes/{Guid.NewGuid()}", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OwnerAloneCanAttachAndHostDisconnectClosesRoutes()
    {
        await using var server = new RelayTestServer();
        using var control = server.Socket();
        await server.ConnectAsync(control, "host");
        var session = await server.ReadIdAsync(control);
        using var guest = server.Socket("guest");
        var joining = server.ConnectAsync(guest, $"sessions/{session}/guest");
        var route = await server.ReadIdAsync(control);
        var attach = $"sessions/{session}/routes/{route}";
        await server.RejectedAsync(attach, HttpStatusCode.Forbidden, "guest");
        await server.RejectedAsync($"sessions/{session}/routes/{Guid.NewGuid()}", HttpStatusCode.NotFound);
        using var host = server.Socket();
        await server.ConnectAsync(host, attach);
        await joining;
        await server.RejectedAsync(attach, HttpStatusCode.NotFound);
        await guest.SendAsync(new byte[] { 42 }, WebSocketMessageType.Binary, true, server.Token);
        var bytes = new byte[1];
        Assert.Equal(1, (await host.ReceiveAsync(bytes.AsMemory(), server.Token)).Count);
        Assert.Equal(42, bytes[0]);
        control.Abort();
        await server.ClosedAsync(guest);
        await server.ClosedAsync(host);
        await server.RejectedAsync($"sessions/{session}/guest", HttpStatusCode.NotFound);
        await server.RejectedAsync(attach, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PendingRoutesRemainCountedAfterControlReadAndReleaseAfterTimeout()
    {
        await using var server = new RelayTestServer(options =>
        {
            options.MaxPendingRoutesPerSession = 1;
            options.RouteTimeoutSeconds = 1;
        });
        using var control = server.Socket();
        await server.ConnectAsync(control, "host");
        var session = await server.ReadIdAsync(control);
        using var guest = server.Socket("guest");
        var joining = server.ConnectAsync(guest, $"sessions/{session}/guest");
        var route = await server.ReadIdAsync(control);
        await server.RejectedAsync($"sessions/{session}/guest", HttpStatusCode.ServiceUnavailable, "other");
        await Assert.ThrowsAsync<WebSocketException>(() => joining);
        Assert.Equal(HttpStatusCode.Gone, guest.HttpStatusCode);
        await server.RejectedAsync($"sessions/{session}/routes/{route}", HttpStatusCode.NotFound);
        using var next = server.Socket("next");
        var nextJoin = server.ConnectAsync(next, $"sessions/{session}/guest");
        var nextRoute = await server.ReadIdAsync(control);
        using var host = server.Socket();
        await server.ConnectAsync(host, $"sessions/{session}/routes/{nextRoute}");
        await nextJoin;
    }

    [Fact]
    public async Task ActiveRouteCapRejectsAdditionalGuestsUntilTheRouteCloses()
    {
        await using var server = new RelayTestServer(options => options.MaxRoutesPerSession = 1);
        using var control = server.Socket();
        await server.ConnectAsync(control, "host");
        var session = await server.ReadIdAsync(control);
        using var guest = server.Socket("guest");
        var joining = server.ConnectAsync(guest, $"sessions/{session}/guest");
        var route = await server.ReadIdAsync(control);
        using var host = server.Socket();
        await server.ConnectAsync(host, $"sessions/{session}/routes/{route}");
        await joining;
        await server.RejectedAsync($"sessions/{session}/guest", HttpStatusCode.ServiceUnavailable, "other");
        guest.Abort();
        await server.ClosedAsync(host);
        using var next = server.Socket("next");
        var nextJoin = server.ConnectAsync(next, $"sessions/{session}/guest");
        var nextRoute = await server.ReadIdAsync(control);
        using var nextHost = server.Socket();
        await server.ConnectAsync(nextHost, $"sessions/{session}/routes/{nextRoute}");
        await nextJoin;
    }

    [Theory]
    [InlineData("control")]
    [InlineData("guest")]
    [InlineData("attach")]
    public async Task EveryAuthenticatedSocketClosesAtItsTokenExpiry(string expiring)
    {
        await using var server = new RelayTestServer();
        var expiry = DateTime.UtcNow.AddSeconds(3);
        using var control = server.Socket(expires: expiring == "control" ? expiry : null);
        await server.ConnectAsync(control, "host");
        var session = await server.ReadIdAsync(control);
        using var guest = server.Socket("guest", expiring == "guest" ? expiry : null);
        var joining = server.ConnectAsync(guest, $"sessions/{session}/guest");
        var route = await server.ReadIdAsync(control);
        using var host = server.Socket(expires: expiring == "attach" ? expiry : null);
        await server.ConnectAsync(host, $"sessions/{session}/routes/{route}");
        await joining;
        await server.ClosedAsync(guest);
        await server.ClosedAsync(host);
        if (expiring == "control")
        {
            await server.ClosedAsync(control);
            await server.RejectedAsync($"sessions/{session}/guest", HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task ExpiredTokenCannotUpgradeWithinTheJwtClockSkewAllowance()
    {
        await using var server = new RelayTestServer();
        using var socket = server.Socket(expires: DateTime.UtcNow.AddSeconds(-1));
        await Assert.ThrowsAsync<WebSocketException>(() => server.ConnectAsync(socket, "host"));
        Assert.Equal(HttpStatusCode.Unauthorized, socket.HttpStatusCode);
    }
}
