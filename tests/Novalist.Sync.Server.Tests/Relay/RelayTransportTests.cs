using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Novalist.Sync.Server.Relay;

namespace Novalist.Sync.Server.Tests.Relay;

public sealed class RelayTransportTests
{
    [Theory]
    [InlineData("Production", "http", "203.0.113.1", false, 400)]
    [InlineData("Production", "https", "203.0.113.1", false, 401)]
    [InlineData("Production", "http", "127.0.0.1", false, 400)]
    [InlineData("Development", "http", "127.0.0.1", false, 401)]
    [InlineData("Development", "http", "203.0.113.1", false, 400)]
    [InlineData("Production", "http", "203.0.113.1", true, 400)]
    [InlineData("Production", "http", "192.0.2.10", true, 401)]
    public async Task RelayRequiresTlsOrActualLoopbackDevelopment(string environment, string scheme, string address, bool forwarded, int expected)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Relay:Enabled", "true");
            builder.UseSetting("Relay:KnownProxies:0", "192.0.2.10");
            builder.UseSetting("Authentication:Schemes:Bearer:Authority", "https://idp.example");
            builder.UseSetting("Authentication:Schemes:Bearer:ValidAudiences:0", "novalist");
        });
        using var client = factory.CreateClient();
        var response = await factory.Server.SendAsync(context =>
        {
            context.Request.Method = "GET";
            context.Request.Path = "/relay/v1/host";
            context.Request.Scheme = scheme;
            context.Connection.RemoteIpAddress = IPAddress.Parse(address);
            context.Connection.LocalIpAddress = IPAddress.Loopback;
            if (forwarded)
            {
                context.Request.Headers["X-Forwarded-For"] = "203.0.113.2";
                context.Request.Headers["X-Forwarded-Proto"] = "https";
            }
        }, TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.Response.StatusCode);
    }

    [Fact]
    public async Task RelayForwardsTheFinalPacketAndBothCloseFrames()
    {
        var ct = TestContext.Current.CancellationToken;
        var guest = await SocketPairAsync(ct);
        var host = await SocketPairAsync(ct);
        using var guestClient = guest.Client;
        using var guestRelay = guest.Relay;
        using var hostClient = host.Client;
        using var hostRelay = host.Relay;
        var transfer = RelayPackets.TransferAsync(guestRelay, hostRelay, ct);
        byte[] payload = [1, 2, 3, 4];
        await guestClient.SendAsync(payload.AsMemory(), WebSocketMessageType.Binary, true, ct);
        await guestClient.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "finished", ct);
        var buffer = new byte[16];
        var packet = await hostClient.ReceiveAsync(buffer.AsMemory(), ct);
        Assert.Equal(payload, buffer[..packet.Count]);
        Assert.Equal(WebSocketMessageType.Binary, packet.MessageType);
        var closed = await hostClient.ReceiveAsync(buffer.AsMemory(), ct);
        Assert.Equal(WebSocketMessageType.Close, closed.MessageType);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, hostClient.CloseStatus);
        Assert.Equal("finished", hostClient.CloseStatusDescription);
        await hostClient.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "acknowledged", ct);
        Assert.Equal(WebSocketMessageType.Close, (await guestClient.ReceiveAsync(buffer.AsMemory(), ct)).MessageType);
        Assert.Equal("acknowledged", guestClient.CloseStatusDescription);
        await transfer.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(WebSocketState.Closed, guestRelay.State);
        Assert.Equal(WebSocketState.Closed, hostRelay.State);
    }

    private static async Task<(WebSocket Client, WebSocket Relay)> SocketPairAsync(CancellationToken ct)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, ct);
            var server = await listener.AcceptTcpClientAsync(ct);
            return (WebSocket.CreateFromStream(client.GetStream(), false, null, Timeout.InfiniteTimeSpan),
                WebSocket.CreateFromStream(server.GetStream(), true, null, Timeout.InfiniteTimeSpan));
        }
        catch { client.Dispose(); throw; }
    }
}
