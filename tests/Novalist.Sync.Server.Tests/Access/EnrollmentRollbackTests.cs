using System.Data.Common;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Access;
using Novalist.Sync.Server.Tests.Infrastructure;

namespace Novalist.Sync.Server.Tests.Access;

public sealed class EnrollmentRollbackTests(DatabaseFixture database)
{
    [Fact]
    public async Task FailedPersistenceRollsBackBothDevicesAndAllowsTheSameBrowserProofToRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        var failure = new FailDeviceWriteOnce();
        await using var server = new SyncServer(database, commands: failure).WithWebHostBuilder(builder =>
            builder.UseSetting("Authentication:NativeClient:ClientId", "novalist"));
        using var http = server.CreateClient();
        var subject = Guid.NewGuid().ToString();
        http.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.Create(subject));
        var account = await http.GetFromJsonAsync<DeviceRegistrationContext>(Protocol.DeviceRegistrationPath, ct);
        Assert.NotNull(account);
        using var device = new TestRecoveryDevice();
        using var recovery = new TestRecoveryDevice();
        using var response = await http.PostAsJsonAsync(Protocol.DeviceEnrollmentPath, new DeviceEnrollmentRequest(device.Keys, recovery.Keys), ct);
        var challenge = await response.Content.ReadFromJsonAsync<DeviceEnrollmentChallenge>(ct);
        Assert.NotNull(challenge);
        var identity = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = TestTokens.Issuer,
            Audience = "novalist",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["nonce"] = challenge.Nonce,
                ["amr"] = new[] { "mfa" },
                ["auth_time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            },
            SigningCredentials = new(TestTokens.SigningKey, SecurityAlgorithms.HmacSha256)
        });
        var request = TestEnrollment.First(account.UserId, device.Keys, device.Signing, recovery) with { EnrollmentProof = new(challenge.Id, identity) };
        var principal = TestTokens.Principal(("iss", TestTokens.Issuer), ("sub", subject));
        await using (var scope = server.Services.CreateAsyncScope())
        {
            var enrollment = scope.ServiceProvider.GetRequiredService<VerifiedDeviceEnrollment>();
            await Assert.ThrowsAsync<DbUpdateException>(() => enrollment.RegisterAsync(principal, request, SyncServer.PublicUrl, ct));
        }
        Assert.True(failure.Failed);
        await using (var db = database.CreateContext())
            Assert.Equal(0, await db.Devices.CountAsync(d => d.UserId == account.UserId, ct));
        await using (var scope = server.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<VerifiedDeviceEnrollment>().RegisterAsync(principal, request, SyncServer.PublicUrl, ct);
        await using (var db = database.CreateContext())
            Assert.Equal(2, await db.Devices.CountAsync(d => d.UserId == account.UserId, ct));
        await using (var scope = server.Services.CreateAsyncScope())
            await Assert.ThrowsAsync<AccessDeniedException>(() => scope.ServiceProvider.GetRequiredService<VerifiedDeviceEnrollment>()
                .RegisterAsync(principal, request, SyncServer.PublicUrl, ct));
    }

    private sealed class FailDeviceWriteOnce : DbCommandInterceptor
    {
        public bool Failed { get; private set; }
        private void AfterWrite(DbCommand command)
        {
            if (Failed || !command.CommandText.Contains("INSERT INTO devices", StringComparison.Ordinal)) return;
            Failed = true;
            throw new InvalidOperationException("Injected failure after the device write.");
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        { AfterWrite(command); return ValueTask.FromResult(result); }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        { AfterWrite(command); return ValueTask.FromResult(result); }
    }
}
