using Microsoft.EntityFrameworkCore;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Access;
using Novalist.Sync.Server.Data;
using Novalist.Sync.Server.Tests.Infrastructure;

namespace Novalist.Sync.Server.Tests.Access;

public sealed class FieldLeaseRetentionTests(DatabaseFixture database)
{
    [Fact]
    public async Task CleanupDeletesExpiredAttemptsWithoutAllowingTheirAcquisitionToReplay()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await database.CreateUserAsync();
        var device = await database.CreateDeviceAsync(user.Id);
        var project = await database.CreateProjectAsync((user.Id, MembershipRole.Owner));
        var expired = DateTimeOffset.UtcNow.AddMinutes(-1);
        var request = new FieldLeaseRequest(project, Guid.NewGuid(), 0, Guid.NewGuid(), expired);
        await using var db = database.CreateContext();
        db.FieldLeaseAttempts.Add(new() { ProjectId = project, DeviceId = device, LeaseId = request.LeaseId, ExpiresAt = expired });
        await db.SaveChangesAsync(ct);
        await new EphemeralStateCleanup(db).PruneAsync(ct);
        Assert.Empty(await db.FieldLeaseAttempts.Where(value => value.ProjectId == project).ToListAsync(ct));
        var leases = new FieldLeaseAccess(db, new(db, database.DeliveryGate), new(db));
        await Assert.ThrowsAsync<ConflictException>(() => leases.AcquireAsync(user.Id, device, request, ct));
        await Assert.ThrowsAsync<FormatException>(() => leases.AcquireAsync(user.Id, device, request with { AcquireBefore = null }, ct));
        await Assert.ThrowsAsync<FormatException>(() => leases.AcquireAsync(user.Id, device, request with { AcquireBefore = DateTimeOffset.UtcNow.AddHours(1) }, ct));
    }

    [Fact]
    public async Task PerDeviceAttemptLimitIsBoundedAndExpiredRowsMakeRoomForNewFocus()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await database.CreateUserAsync();
        var device = await database.CreateDeviceAsync(user.Id);
        var otherDevice = await database.CreateDeviceAsync(user.Id);
        var project = await database.CreateProjectAsync((user.Id, MembershipRole.Owner));
        var deadline = DateTimeOffset.UtcNow.AddMinutes(1);
        await using var db = database.CreateContext();
        db.FieldLeaseAttempts.AddRange(Enumerable.Range(0, FieldLeaseAccess.MaximumRetainedAttempts).Select(_ => new FieldLeaseAttempt
        {
            ProjectId = project,
            DeviceId = device,
            LeaseId = Guid.NewGuid(),
            ExpiresAt = deadline
        }));
        await db.SaveChangesAsync(ct);
        var leases = new FieldLeaseAccess(db, new(db, database.DeliveryGate), new(db));
        var request = new FieldLeaseRequest(project, Guid.NewGuid(), 0, Guid.NewGuid(), deadline);
        await Assert.ThrowsAsync<ConflictException>(() => leases.AcquireAsync(user.Id, device, request, ct));
        Assert.Equal(otherDevice, (await leases.AcquireAsync(user.Id, otherDevice, request with { ResourceId = Guid.NewGuid() }, ct)).DeviceId);
        await db.FieldLeaseAttempts.Where(value => value.ProjectId == project && value.DeviceId == device)
            .ExecuteUpdateAsync(set => set.SetProperty(value => value.ExpiresAt, DateTimeOffset.UnixEpoch), ct);
        Assert.Equal(device, (await leases.AcquireAsync(user.Id, device, request, ct)).DeviceId);
        Assert.Equal(1, await db.FieldLeaseAttempts.CountAsync(value => value.ProjectId == project && value.DeviceId == device, ct));
    }
}
