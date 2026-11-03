using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Access;
using Novalist.Sync.Server.Hubs;
using Novalist.Sync.Server.Tests.Infrastructure;
using NSec.Cryptography;

namespace Novalist.Sync.Server.Tests.Access;

public sealed class DeviceReviewRegressionTests(DatabaseFixture database)
{
    [Fact]
    public async Task RegistrationContextRejectsAnAuthenticatedTokenWithoutASubject()
    {
        await using var server = new SyncServer(database);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = TestTokens.Issuer,
            Audience = TestTokens.Audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new(TestTokens.SigningKey, SecurityAlgorithms.HmacSha256)
        });
        using var http = server.CreateClient(token);
        using var response = await http.GetAsync(Protocol.DeviceRegistrationPath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("signing")]
    [InlineData("receiving")]
    public async Task LinkedDevicesCannotReuseAnotherIdentitysKeys(string reused)
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await database.CreateUserAsync();
        using var root = new TestRecoveryDevice();
        using var recovery = new TestRecoveryDevice();
        using var fresh = new TestRecoveryDevice();
        await using (var db = database.CreateContext())
            await new DeviceRegistry(db, database.DeliveryGate).RegisterVerifiedAsync(user.Id,
                TestEnrollment.First(user.Id, root.Keys, root.Signing, recovery), SyncServer.PublicUrl, _ => { }, ct);
        var keys = fresh.Keys with
        {
            SigningPublicKey = reused == "signing" ? root.Keys.SigningPublicKey : fresh.Keys.SigningPublicKey,
            ReceivingPublicKey = reused == "receiving" ? root.Keys.ReceivingPublicKey : fresh.Keys.ReceivingPublicKey
        };
        var request = new VerifiedDeviceRegistration(keys,
            SignatureAlgorithm.Ed25519.Sign(reused == "signing" ? root.Signing : fresh.Signing,
                DeviceProof.EncodeRegistration(SyncServer.PublicUrl, user.Id, keys)), root.Keys.DeviceId,
            SignatureAlgorithm.Ed25519.Sign(root.Signing, DeviceProof.EncodeLink(SyncServer.PublicUrl, user.Id, root.Keys.DeviceId, keys)));
        await using var rejected = database.CreateContext();
        var error = await Assert.ThrowsAsync<ConflictException>(() => new DeviceRegistry(rejected, database.DeliveryGate)
            .RegisterVerifiedAsync(user.Id, request, SyncServer.PublicUrl, _ => { }, ct));
        Assert.Contains("keys cannot be reused", error.Message);
        Assert.Equal(2, await rejected.Devices.CountAsync(d => d.UserId == user.Id, ct));
    }

    [Fact]
    public async Task EnrollmentRetryFinishesLegacyCutoverWithoutAbortingVerifiedConnections()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await database.CreateUserAsync();
        using var device = new TestRecoveryDevice();
        using var recovery = new TestRecoveryDevice();
        var request = TestEnrollment.First(user.Id, device.Keys, device.Signing, recovery);
        await using var server = new SyncServer(database);
        using var http = server.CreateClient(TestTokens.Enrollment(user.Subject, device.Keys.SigningPublicKey));
        using var first = await http.PutAsJsonAsync(Protocol.VerifiedDevicesPath, request, ct);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        var connections = server.Services.GetRequiredService<ConnectionRegistry>();
        var legacy = new FakeHubCallerContext();
        var verified = new FakeHubCallerContext();
        verified.Items["deviceAuthenticated"] = true;
        connections.Add(legacy, user.Id, Guid.NewGuid());
        connections.Add(verified, user.Id, device.Keys.DeviceId);
        using var retry = await http.PutAsJsonAsync(Protocol.VerifiedDevicesPath, request, ct);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.True(legacy.Aborted);
        Assert.False(verified.Aborted);
    }

    [Fact]
    public void ClaimedProofRejectsConcurrentUseAndIsOnlyConsumedAfterSuccess()
    {
        var user = Guid.NewGuid();
        using var device = new TestRecoveryDevice();
        using var recovery = new TestRecoveryDevice();
        var challenges = new DeviceEnrollmentChallenges(TimeProvider.System);
        var challenge = challenges.Create(user, SyncServer.PublicUrl, new(device.Keys, recovery.Keys));
        var request = TestEnrollment.First(user, device.Keys, device.Signing, recovery) with { EnrollmentProof = new(challenge.Id, "identity-token") };
        using (var claim = challenges.Claim(user, SyncServer.PublicUrl, request))
            Assert.Throws<AccessDeniedException>(() => challenges.Claim(user, SyncServer.PublicUrl, request));
        using (var retry = challenges.Claim(user, SyncServer.PublicUrl, request)) retry.Complete();
        Assert.Throws<AccessDeniedException>(() => challenges.Claim(user, SyncServer.PublicUrl, request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://unsafe.example")]
    public async Task UnconfiguredDeviceEndpointsReturnUnavailable(string serverUrl)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = new SyncServer(database).WithWebHostBuilder(builder => builder.UseSetting("Collaboration:ServerUrl", serverUrl));
        using var http = server.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.Create(Guid.NewGuid().ToString()));
        using var device = new TestRecoveryDevice();
        using var recovery = new TestRecoveryDevice();
        using var context = await http.GetAsync(Protocol.DeviceRegistrationPath, ct);
        using var challenge = await http.PostAsJsonAsync(Protocol.DeviceEnrollmentPath, new DeviceEnrollmentRequest(device.Keys, recovery.Keys), ct);
        using var enrollment = await http.PutAsJsonAsync(Protocol.VerifiedDevicesPath,
            TestEnrollment.First(Guid.NewGuid(), device.Keys, device.Signing, recovery), ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, context.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, challenge.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, enrollment.StatusCode);
    }
}
