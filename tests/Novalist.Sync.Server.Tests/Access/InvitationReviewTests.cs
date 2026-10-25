using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Access;
using Novalist.Sync.Server.Data;
using Novalist.Sync.Server.Tests.Infrastructure;
using Npgsql;

namespace Novalist.Sync.Server.Tests.Access;

public sealed class InvitationReviewTests(DatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WrongProjectRevocationIsAConflictAndKeepsTheInvitationUsable()
    {
        await using var server = new SyncServer(database);
        var owner = await database.CreateUserAsync();
        var first = await database.CreateProjectAsync((owner.Id, MembershipRole.Owner));
        var second = await database.CreateProjectAsync((owner.Id, MembershipRole.Owner));
        using var http = server.CreateClient(TestTokens.Create(owner.Subject));
        var request = new CreateInvitation(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1));
        using var created = await http.PostAsJsonAsync($"/projects/{first}/invitations", request, Ct);
        created.EnsureSuccessStatusCode();
        using var wrong = await http.PostAsJsonAsync($"/projects/{second}/invitations/revoke", request, Ct);
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        using var redeemed = await http.PostAsJsonAsync("/invitations/redeem", request, Ct);
        Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
    }

    [Fact]
    public async Task ExpiryMustRemainInTheFutureAfterDatabasePrecisionNormalization()
    {
        var owner = await database.CreateUserAsync();
        var project = await database.CreateProjectAsync((owner.Id, MembershipRole.Owner));
        var clock = new FixedClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(3));
        await using var db = database.CreateContext();
        var access = new InvitationAccess(db, new(db, database.DeliveryGate), clock);
        await Assert.ThrowsAsync<ArgumentException>(() => access.CreateAsync(owner.Id, project,
            new(Guid.NewGuid(), clock.GetUtcNow().AddTicks(1)), Ct));
        Assert.False(await db.Invitations.AnyAsync(value => value.ProjectId == project, Ct));
    }

    [Fact]
    public async Task LegacyClaimantCanPopulateAnEmptyActivationReceiptButCannotReplaceIt()
    {
        var owner = await database.CreateUserAsync();
        var guest = await database.CreateUserAsync();
        var project = await database.CreateProjectAsync((owner.Id, MembershipRole.Owner));
        var device = await database.CreateDeviceAsync(guest.Id);
        await using var db = database.CreateContext();
        var access = new InvitationAccess(db, new(db, database.DeliveryGate), TimeProvider.System);
        var id = Guid.NewGuid();
        await access.CreateAsync(owner.Id, project, new(id, DateTimeOffset.UtcNow.AddHours(1)), Ct);
        await access.RedeemAsync(guest.Id, id, Ct, legacyAccount: true);
        using var identity = new TestRecoveryDevice();
        await db.Devices.Where(value => value.Id == device).ExecuteUpdateAsync(
            set => set.SetProperty(value => value.SigningPublicKey, identity.Keys.SigningPublicKey), Ct);
        var request = new ActivateInvitation(id, new byte[32]);
        var receipt = await access.ActivateAsync(guest.Id, device, request, Ct);
        Assert.Equal(project, receipt.ProjectId);
        Assert.Equal(device, receipt.DeviceId);
        await Assert.ThrowsAsync<ConflictException>(() => access.ActivateAsync(guest.Id, device,
            request with { CertificateHash = Enumerable.Repeat((byte)1, 32).ToArray() }, Ct));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("redeem")]
    [InlineData("revoke")]
    public async Task WaitingInvitationMutationRechecksADeviceRevokedUnderTheAccountLock(string operation)
    {
        var user = await database.CreateUserAsync();
        var device = await database.CreateDeviceAsync(user.Id);
        var project = await database.CreateProjectAsync((user.Id, MembershipRole.Owner));
        await using var revocation = database.CreateContext();
        await using var transaction = await revocation.Database.BeginTransactionAsync(Ct);
        await revocation.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {user.Id} FOR UPDATE").ToListAsync(Ct);
        await using var mutation = database.CreateContext();
        await mutation.Database.OpenConnectionAsync(Ct);
        var process = ((NpgsqlConnection)mutation.Database.GetDbConnection()).ProcessID;
        var access = new InvitationAccess(mutation, new(mutation, database.DeliveryGate), TimeProvider.System);
        var id = Guid.NewGuid();
        Task pending = operation switch
        {
            "create" => access.CreateAsync(user.Id, project, new(id, DateTimeOffset.UtcNow.AddHours(1)), Ct, deviceId: device),
            "redeem" => access.RedeemAsync(user.Id, id, Ct, deviceId: device),
            _ => access.RevokeAsync(user.Id, project, id, Ct, deviceId: device)
        };
        try
        {
            await DatabaseLocks.WaitUntilBlockedAsync(database, process, Ct);
            await revocation.Devices.Where(value => value.Id == device).ExecuteUpdateAsync(
                set => set.SetProperty(value => value.RevokedAt, DateTimeOffset.UtcNow), Ct);
            await transaction.CommitAsync(Ct);
            var error = await Assert.ThrowsAsync<AccessDeniedException>(() => pending);
            Assert.Contains("device is no longer active", error.Message);
        }
        finally
        {
            await transaction.DisposeAsync();
            await Assert.ThrowsAsync<AccessDeniedException>(() => pending);
        }
    }

    [Fact]
    public async Task RemovingANonmemberDoesNotExpireInvitationsOrNotifyConnections()
    {
        var owner = await database.CreateUserAsync();
        var outsider = await database.CreateUserAsync();
        var project = await database.CreateProjectAsync((owner.Id, MembershipRole.Owner));
        await using var db = database.CreateContext();
        var projects = new ProjectAccess(db, database.DeliveryGate);
        var id = Guid.NewGuid();
        await new InvitationAccess(db, projects, TimeProvider.System).CreateAsync(owner.Id, project,
            new(id, DateTimeOffset.UtcNow.AddHours(1)), Ct);
        var notified = false;
        Assert.False(await projects.RemoveMemberAsync(owner.Id, project, outsider.Id, Ct,
            () => { notified = true; return Task.CompletedTask; }));
        Assert.False(notified);
        Assert.True((await db.Invitations.AsNoTracking().SingleAsync(value => value.Id == id, Ct)).ExpiresAt > DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<AccessDeniedException>(() => projects.RemoveMemberAsync(outsider.Id, project, outsider.Id, Ct));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task RemovalHoldsTheDeliveryBarrierUntilConnectionNotificationCompletes()
    {
        var owner = await database.CreateUserAsync();
        var member = await database.CreateUserAsync();
        var project = await database.CreateProjectAsync((owner.Id, MembershipRole.Owner), (member.Id, MembershipRole.Contributor));
        await using var db = database.CreateContext();
        var access = new ProjectAccess(db, database.DeliveryGate);
        Task<IAsyncDisposable>? delivery = null;
        try
        {
            Assert.True(await access.RemoveMemberAsync(owner.Id, project, member.Id, Ct, async () =>
            {
                await using var verify = database.CreateContext();
                Assert.False(await verify.Memberships.AnyAsync(value => value.ProjectId == project && value.UserId == member.Id, Ct));
                delivery = access.PauseDeliveryAsync(project, Ct).AsTask();
                Assert.False(delivery.IsCompleted, "Delivery must wait until the connection has been removed from its group.");
            }));
        }
        finally
        {
            if (delivery is not null) await (await delivery.WaitAsync(Ct)).DisposeAsync();
        }
    }
}
