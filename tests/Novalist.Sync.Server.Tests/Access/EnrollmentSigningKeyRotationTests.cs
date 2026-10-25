using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Access;
using Novalist.Sync.Server.Tests.Infrastructure;

namespace Novalist.Sync.Server.Tests.Access;

public sealed class EnrollmentSigningKeyRotationTests
{
    [Fact]
    public async Task UnknownSigningKeyRefreshesProviderMetadataBeforeRejectingEnrollment()
    {
        var signing = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = "new-key" };
        var manager = new RotatingKeys(signing);
        var services = new ServiceCollection();
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.ConfigurationManager = manager);
        using var provider = services.BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Authentication:NativeClient:ClientId"] = "novalist" }).Build();
        var validator = new DeviceEnrollmentTokens(provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>(), configuration,
            new DeviceEnrollmentPolicy(TimeProvider.System));
        var challenge = new DeviceEnrollmentChallenge(Guid.NewGuid(), "nonce", DateTimeOffset.UtcNow.AddMinutes(5));
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = TestTokens.Issuer,
            Audience = "novalist",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "owner",
                ["nonce"] = challenge.Nonce,
                ["amr"] = new[] { "mfa" },
                ["auth_time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            },
            SigningCredentials = new(signing, SecurityAlgorithms.HmacSha256)
        });
        await validator.ValidateAsync(TestTokens.Principal(("iss", TestTokens.Issuer), ("sub", "owner")), challenge, token,
            TestContext.Current.CancellationToken);
        Assert.True(manager.Refreshes > 0);
    }

    private sealed class RotatingKeys : BaseConfigurationManager, IConfigurationManager<OpenIdConnectConfiguration>
    {
        private readonly OpenIdConnectConfiguration previous = new() { Issuer = TestTokens.Issuer };
        private readonly OpenIdConnectConfiguration current = new() { Issuer = TestTokens.Issuer };
        public int Refreshes { get; private set; }
        public RotatingKeys(SecurityKey signing)
        {
            previous.SigningKeys.Add(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = "old-key" });
            current.SigningKeys.Add(signing);
        }
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) => Task.FromResult(Refreshes == 0 ? previous : current);
        public override Task<BaseConfiguration> GetBaseConfigurationAsync(CancellationToken cancel) => Task.FromResult<BaseConfiguration>(Refreshes == 0 ? previous : current);
        public override void RequestRefresh() => Refreshes++;
    }
}
