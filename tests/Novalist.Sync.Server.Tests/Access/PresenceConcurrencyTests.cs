using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Access;
using Novalist.Sync.Server.Data;
using Novalist.Sync.Server.Tests.Infrastructure;
using Novalist.Sync.Server.Updates;
using Npgsql;

namespace Novalist.Sync.Server.Tests.Access;

public sealed class PresenceConcurrencyTests(DatabaseFixture database)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RotationDuringPreflightRejectsStaleOrRemovedSendersBeforeDelivery(bool removeSender)
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await database.CreateUserAsync();
        var guest = await database.CreateUserAsync();
        var ownerDevice = await database.CreateDeviceAsync(owner.Id);
        var guestDevice = await database.CreateDeviceAsync(guest.Id);
        var project = await database.CreateProjectAsync((owner.Id, MembershipRole.Owner), (guest.Id, MembershipRole.Contributor));
        await using var rotating = database.CreateContext();
        var epochs = new ProjectEpochAccess(rotating, new(rotating));
        await epochs.RegisterAsync(owner.Id, ownerDevice, project, 0, RandomNumberGenerator.GetBytes(182), ct);
        var rotation = Guid.NewGuid();
        await epochs.BeginAsync(owner.Id, ownerDevice, new(project, rotation, 0, 0), ct);
        var checkpoint = TestData.Envelope(project, ownerDevice, 1);
        checkpoint = checkpoint with { Header = (EnvelopeHeader.Parse(checkpoint.Header) with { Epoch = 1 }).ToBytes() };
        await epochs.StageAsync(owner.Id, ownerDevice, rotation, checkpoint, ct);
        var pause = new SqlCommandPause("FOR SHARE");
        await using var sending = database.CreateContext(pause);
        var presence = new PresenceAccess(sending, new(sending), new(sending));
        var sender = removeSender ? guest.Id : owner.Id;
        var device = removeSender ? guestDevice : ownerDevice;
        var delivered = false;
        var delivery = presence.DeliverAsync(sender, device, TestData.Envelope(project, device, 1, MsgType.Presence),
            (_, _) => { delivered = true; return Task.CompletedTask; }, ct);
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            await epochs.CommitAsync(owner.Id, ownerDevice, new(rotation, guest.Id,
                [new(ownerDevice, RandomNumberGenerator.GetBytes(182), RandomNumberGenerator.GetBytes(128))]), ct);
            pause.Release.TrySetResult();
            if (removeSender) await Assert.ThrowsAsync<AccessDeniedException>(() => delivery);
            else await Assert.ThrowsAsync<PushRejectedException>(() => delivery);
            Assert.False(delivered);
        }
        finally { pause.Release.TrySetResult(); }
    }

    [Fact]
    public async Task RotationWaitsForPresenceDeliveryAndThenExcludesTheRemovedMember()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await database.CreateUserAsync();
        var guest = await database.CreateUserAsync();
        var device = await database.CreateDeviceAsync(owner.Id);
        var guestDevice = await database.CreateDeviceAsync(guest.Id);
        var project = await database.CreateProjectAsync((owner.Id, MembershipRole.Owner), (guest.Id, MembershipRole.Contributor));
        await using var rotating = database.CreateContext();
        var epochs = new ProjectEpochAccess(rotating, new(rotating));
        await epochs.RegisterAsync(owner.Id, device, project, 0, RandomNumberGenerator.GetBytes(182), ct);
        var rotation = Guid.NewGuid();
        await epochs.BeginAsync(owner.Id, device, new(project, rotation, 0, 0), ct);
        var checkpoint = TestData.Envelope(project, device, 1);
        checkpoint = checkpoint with { Header = (EnvelopeHeader.Parse(checkpoint.Header) with { Epoch = 1 }).ToBytes() };
        await epochs.StageAsync(owner.Id, device, rotation, checkpoint, ct);
        await rotating.Database.OpenConnectionAsync(ct);
        var process = ((NpgsqlConnection)rotating.Database.GetDbConnection()).ProcessID;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sending = database.CreateContext();
        var presence = new PresenceAccess(sending, new(sending), new(sending));
        var envelope = TestData.Envelope(project, device, 1, MsgType.Presence);
        var delivery = presence.DeliverAsync(owner.Id, device, envelope, async (id, recipients) =>
        {
            Assert.Equal(project, id);
            Assert.Contains(guestDevice, recipients);
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        }, ct);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            var commit = epochs.CommitAsync(owner.Id, device, new(rotation, guest.Id,
                [new(device, RandomNumberGenerator.GetBytes(182), RandomNumberGenerator.GetBytes(128))]), ct);
            await DatabaseLocks.WaitUntilBlockedAsync(database, process, ct);
            release.TrySetResult();
            await delivery;
            await commit;
            await Assert.ThrowsAsync<PushRejectedException>(() => presence.DeliverAsync(owner.Id, device, envelope,
                (_, _) => throw new InvalidOperationException("Stale presence was delivered."), ct));
            var current = envelope with { Header = (EnvelopeHeader.Parse(envelope.Header) with { Epoch = 1 }).ToBytes() };
            await presence.DeliverAsync(owner.Id, device, current, (_, recipients) =>
            {
                Assert.Contains(device, recipients);
                Assert.DoesNotContain(guestDevice, recipients);
                return Task.CompletedTask;
            }, ct);
        }
        finally { release.TrySetResult(); }
    }
}
