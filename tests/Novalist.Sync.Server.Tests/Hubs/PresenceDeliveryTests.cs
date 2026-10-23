using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Data;
using Novalist.Sync.Server.Hubs;
using Novalist.Sync.Server.Tests.Infrastructure;

namespace Novalist.Sync.Server.Tests.Hubs;

public sealed class PresenceDeliveryTests(DatabaseFixture database)
{
    [Fact]
    public async Task SlowPresenceReceivesSignalRCancellationAndDoesNotPreventTheNextEdit()
    {
        var ct = TestContext.Current.CancellationToken;
        using var lifetime = new SlowPresenceLifetimeManager();
        await using var server = new SyncServer(database, lifetime: lifetime);
        var owner = await database.CreateUserAsync();
        var guest = await database.CreateUserAsync();
        var device = await database.CreateDeviceAsync(owner.Id);
        var guestDevice = await database.CreateDeviceAsync(guest.Id);
        var project = await database.CreateProjectAsync((owner.Id, MembershipRole.Owner), (guest.Id, MembershipRole.Contributor));
        await using var host = server.CreateHubConnection(TestTokens.Create(owner.Subject), device);
        await using var recipient = server.CreateHubConnection(TestTokens.Create(guest.Subject), guestDevice);
        await host.StartAsync(ct);
        await recipient.StartAsync(ct);
        await recipient.InvokeAsync("JoinProject", project, ct);
        var error = await Assert.ThrowsAsync<HubException>(() => host.InvokeAsync("PublishPresence",
            TestData.Envelope(project, device, 1, MsgType.Presence), ct).WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Contains("Presence delivery timed out.", error.Message);
        Assert.True(lifetime.Cancelled);
        Assert.Equal([recipient.ConnectionId!], lifetime.Recipients);
        Assert.Equal(1, await host.InvokeAsync<long>("Push", TestData.Envelope(project, device, 2), ct));
    }

    private sealed class SlowPresenceLifetimeManager() : DefaultHubLifetimeManager<SyncHub>(NullLogger<DefaultHubLifetimeManager<SyncHub>>.Instance), IDisposable
    {
        private readonly CancellationTokenSource stopping = new();
        public bool Cancelled { get; private set; }
        public IReadOnlyList<string> Recipients { get; private set; } = [];

        public override async Task SendConnectionsAsync(IReadOnlyList<string> connectionIds, string methodName, object?[] args,
            CancellationToken cancellationToken = default)
        {
            if (methodName != nameof(ISyncClient.ReceivePresence))
            {
                await base.SendConnectionsAsync(connectionIds, methodName, args, cancellationToken);
                return;
            }
            Assert.True(cancellationToken.CanBeCanceled);
            Recipients = connectionIds;
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, lifetime.Token); }
            finally { Cancelled = cancellationToken.IsCancellationRequested; }
        }

        public void Dispose() { stopping.Cancel(); stopping.Dispose(); }
    }
}
