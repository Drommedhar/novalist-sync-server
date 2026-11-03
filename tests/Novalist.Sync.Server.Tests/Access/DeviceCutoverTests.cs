using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Tests.Infrastructure;
using NSec.Cryptography;
using Npgsql;

namespace Novalist.Sync.Server.Tests.Access;

public sealed class DeviceCutoverTests(DatabaseFixture database)
{
    [Fact]
    public async Task VerifiedEnrollmentWaitsForAnAuthorizedLegacyMutationToFinish()
    {
        var ct = TestContext.Current.CancellationToken;
        var enrollmentConnection = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observingEnrollment = false;
        var pause = new SqlCommandPause("INSERT INTO invitations", command =>
        {
            if (observingEnrollment && command.CommandText.Contains("SELECT * FROM users", StringComparison.Ordinal) &&
                command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal) && command.Connection is NpgsqlConnection connection)
                enrollmentConnection.TrySetResult(connection.ProcessID);
        });
        await using var server = new SyncServer(database, commands: pause);
        var token = TestTokens.Create(Guid.NewGuid().ToString());
        using var http = server.CreateClient(token);
        var legacyId = Guid.NewGuid();
        using var registered = await http.PutAsync($"/devices/{legacyId}", null, ct);
        registered.EnsureSuccessStatusCode();
        await using var hub = server.CreateHubConnection(token, legacyId);
        await hub.StartAsync(ct);
        var project = Guid.NewGuid();
        await hub.InvokeAsync("CreateProject", project, ct);
        var account = await http.GetFromJsonAsync<DeviceRegistrationContext>("/devices/registration", ct);
        Assert.NotNull(account);
        using var signing = Key.Create(SignatureAlgorithm.Ed25519);
        using var recoveryDevice = new TestRecoveryDevice();
        using var receiving = Key.Create(KeyAgreementAlgorithm.X25519);
        var keys = new DevicePublicKeys(Guid.NewGuid(), signing.PublicKey.Export(KeyBlobFormat.RawPublicKey), receiving.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        var proof = TestEnrollment.First(account.UserId, keys, signing, recoveryDevice);
        TestEnrollment.Authorize(http, keys.SigningPublicKey);
        var create = http.PostAsJsonAsync($"/projects/{project}/invitations", new CreateInvitation(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)), ct);
        Task<HttpResponseMessage>? upgrade = null;
        try
        {
            var entered = await Task.WhenAny(pause.Entered.Task, create).WaitAsync(TimeSpan.FromSeconds(5), ct);
            await entered;
            if (create.IsCompleted) (await create).EnsureSuccessStatusCode();
            Assert.True(pause.Entered.Task.IsCompleted, "Creation must reach the invitation insert.");
            observingEnrollment = true;
            upgrade = http.PutAsJsonAsync("/devices/verified", proof, ct);
            var processId = await enrollmentConnection.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            await DatabaseLocks.WaitUntilBlockedAsync(database, processId, ct);
            Assert.False(upgrade.IsCompleted, "Enrollment must wait until the already-authorized legacy mutation commits.");
            pause.Release.TrySetResult();
            using var created = await create.WaitAsync(ct);
            using var upgraded = await upgrade.WaitAsync(ct);
            Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, upgraded.StatusCode);
        }
        finally
        {
            pause.Release.TrySetResult();
            using var created = await create;
            if (upgrade is not null) { using var upgraded = await upgrade; }
        }
    }
}
