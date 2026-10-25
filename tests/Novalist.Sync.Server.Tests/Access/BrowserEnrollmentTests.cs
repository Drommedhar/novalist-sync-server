using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Tests.Infrastructure;

namespace Novalist.Sync.Server.Tests.Access;

public sealed class BrowserEnrollmentTests(DatabaseFixture database)
{
    [Theory]
    [InlineData(null)]
    [InlineData("nonce")]
    [InlineData("subject")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("missing-mfa")]
    [InlineData("stale")]
    [InlineData("future")]
    [InlineData("keys")]
    [InlineData("recovery")]
    [InlineData("multi-valid")]
    [InlineData("multi-missing-azp")]
    [InlineData("multi-wrong-azp")]
    [InlineData("wrong-azp")]
    public async Task BrowserMfaMustMatchTheAccountAndBothDeviceKeys(string? invalid)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = new SyncServer(database).WithWebHostBuilder(builder =>
            builder.UseSetting("Authentication:NativeClient:ClientId", "novalist"));
        using var http = server.CreateClient();
        var subject = Guid.NewGuid().ToString();
        http.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.Create(subject));
        var account = await http.GetFromJsonAsync<DeviceRegistrationContext>(Protocol.DeviceRegistrationPath, ct);
        Assert.NotNull(account);
        using var device = new TestRecoveryDevice();
        using var recovery = new TestRecoveryDevice();
        using var response = await http.PostAsJsonAsync("/devices/enrollment", new DeviceEnrollmentRequest(device.Keys, recovery.Keys), ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var challenge = await response.Content.ReadFromJsonAsync<DeviceEnrollmentChallenge>(ct);
        Assert.NotNull(challenge);
        var claims = new Dictionary<string, object>
        {
            ["sub"] = invalid == "subject" ? "another-account" : subject,
            ["nonce"] = invalid == "nonce" ? "another-challenge" : challenge.Nonce,
            ["amr"] = new[] { invalid == "missing-mfa" ? "pwd" : "mfa" },
            ["auth_time"] = DateTimeOffset.UtcNow.AddMinutes(invalid == "stale" ? -6 : invalid == "future" ? 2 : 0).ToUnixTimeSeconds()
        };
        var multipleAudiences = invalid?.StartsWith("multi-", StringComparison.Ordinal) == true;
        if (multipleAudiences) claims["aud"] = new[] { "novalist", "another-audience" };
        if (invalid is "multi-valid") claims["azp"] = "novalist";
        if (invalid is "multi-wrong-azp" or "wrong-azp") claims["azp"] = "another-client";
        var token = IdentityToken(claims, invalid, multipleAudiences);
        using var substituted = new TestRecoveryDevice();
        var ordinary = invalid == "keys" ? substituted : device;
        var backup = invalid == "recovery" ? substituted : recovery;
        var request = TestEnrollment.First(account.UserId, ordinary.Keys, ordinary.Signing, backup) with
        { EnrollmentProof = new(challenge.Id, token) };
        using var enrolled = await http.PutAsJsonAsync(Protocol.VerifiedDevicesPath, request, ct);
        var accepted = invalid is null or "multi-valid";
        Assert.Equal(accepted ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden, enrolled.StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal(accepted ? 2 : 0, await db.Devices.CountAsync(value => value.UserId == account.UserId && value.SigningPublicKey != null, ct));
        if (!accepted) return;
        using var replayed = await http.PutAsJsonAsync(Protocol.VerifiedDevicesPath, request, ct);
        Assert.Equal(HttpStatusCode.Forbidden, replayed.StatusCode);
        using var retry = await http.PutAsJsonAsync(Protocol.VerifiedDevicesPath, request with { EnrollmentProof = null }, ct);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        using var freshResponse = await http.PostAsJsonAsync(Protocol.DeviceEnrollmentPath, new DeviceEnrollmentRequest(device.Keys, recovery.Keys), ct);
        var fresh = await freshResponse.Content.ReadFromJsonAsync<DeviceEnrollmentChallenge>(ct);
        Assert.NotNull(fresh);
        claims["nonce"] = fresh.Nonce;
        var freshRequest = request with { EnrollmentProof = new(fresh.Id, IdentityToken(claims, invalid, multipleAudiences)) };
        using var existing = await http.PutAsJsonAsync(Protocol.VerifiedDevicesPath, freshRequest, ct);
        Assert.Equal(HttpStatusCode.NoContent, existing.StatusCode);
        using var duplicate = await http.PutAsJsonAsync(Protocol.VerifiedDevicesPath, freshRequest, ct);
        Assert.Equal(HttpStatusCode.Forbidden, duplicate.StatusCode);
    }

    private static string IdentityToken(Dictionary<string, object> claims, string? invalid, bool multipleAudiences) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = invalid == "issuer" ? "https://other.example" : TestTokens.Issuer,
            Audience = multipleAudiences ? null : invalid == "audience" ? TestTokens.Audience : "novalist",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new(invalid == "signature" ? new SymmetricSecurityKey(new byte[32]) : TestTokens.SigningKey, SecurityAlgorithms.HmacSha256)
        });
}
