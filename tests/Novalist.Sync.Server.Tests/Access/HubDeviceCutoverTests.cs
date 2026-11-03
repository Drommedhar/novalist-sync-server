using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Novalist.Sync.Server.Access;
using Novalist.Sync.Server.Data;
using Novalist.Sync.Server.Tests.Infrastructure;

namespace Novalist.Sync.Server.Tests.Access;

public sealed class HubDeviceCutoverTests(DatabaseFixture database)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstEnrollmentWaitsForAnAlreadyAuthorizedLegacyMutation(bool push)
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await database.CreateUserAsync();
        var legacy = await database.CreateDeviceAsync(user.Id);
        var project = await database.CreateProjectAsync((user.Id, MembershipRole.Owner));
        var pause = new SqlCommandPause(push ? "INSERT INTO updates" : "INSERT INTO projects");
        await using var server = new SyncServer(database, commands: pause);
        await using var hub = server.CreateHubConnection(TestTokens.Create(user.Subject), legacy);
        await hub.StartAsync(ct);
        var created = Guid.NewGuid();
        Task mutation = push ? hub.InvokeAsync<long>("Push", TestData.Envelope(project, legacy, 1), ct)
            : hub.InvokeAsync("CreateProject", created, ct);
        using var device = new TestRecoveryDevice();
        using var recovery = new TestRecoveryDevice();
        await using var enrollmentDb = database.CreateContext();
        var registry = new DeviceRegistry(enrollmentDb, server.Services.GetRequiredService<AccessDeliveryGate>());
        Task<DeviceRegistrationChange>? enrollment = null;
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            enrollment = registry.RegisterVerifiedAsync(user.Id, TestEnrollment.First(user.Id, device.Keys, device.Signing, recovery),
                SyncServer.PublicUrl, _ => { }, ct);
            Assert.False(enrollment.IsCompleted);
            pause.Release.TrySetResult();
            await mutation;
            Assert.Equal(DeviceRegistrationChange.FirstVerified, await enrollment);
            await using var verify = database.CreateContext();
            if (push) Assert.Single(await verify.Updates.Where(update => update.ProjectId == project).ToListAsync(ct));
            else Assert.True(await verify.Projects.AnyAsync(value => value.Id == created, ct));
            var error = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("CreateProject", Guid.NewGuid(), ct));
            Assert.Contains("not an active device", error.Message);
        }
        finally
        {
            pause.Release.TrySetResult();
            await mutation;
            if (enrollment is not null) await enrollment;
        }
    }
}
