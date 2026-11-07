using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Novalist.Sync.Server.Relay;
using Novalist.Sync.Server.Tests.Infrastructure;

namespace Novalist.Sync.Server.Tests.Relay;

public sealed class RelayAdmissionTests
{
    [Fact]
    public async Task AuthenticatedRelayRequiresRoomForBothAddressAndAccountQuotas()
    {
        using var host = CreateConfigurationHost(new()
        {
            ["Relay:Enabled"] = "true",
            ["Relay:MaxQuotaEntries"] = "1",
            ["Relay:Introspection:Endpoint"] = "https://idp.test/introspect",
            ["Relay:Introspection:ClientId"] = "test",
            ["Relay:Introspection:ClientSecret"] = "test"
        });
        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("two quota entries", error.Message);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public async Task GlobalUpgradeAllowanceBelowThreeFailsValidation(string allowance)
    {
        using var host = CreateConfigurationHost(new()
        {
            ["Relay:Enabled"] = "true",
            ["Relay:RequireAuthentication"] = "false",
            ["Relay:RequestsPerMinute"] = allowance
        });
        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("RequestsPerMinute", error.Message);
    }

    [Fact]
    public async Task UnauthenticatedTrafficCannotConsumeTheGlobalUpgradeAllowance()
    {
        await using var server = new RelayTestServer(options => options.RequestsPerMinute = 3);
        await server.RejectedAsync("host", HttpStatusCode.Unauthorized, null);
        await server.RejectedAsync("host", HttpStatusCode.Unauthorized, null);
        using var control = server.Socket();
        await server.ConnectAsync(control, "host");
        await server.ReadIdAsync(control);
        using var second = server.Socket("second");
        await server.ConnectAsync(second, "host");
        await server.ReadIdAsync(second);
        using var third = server.Socket("third");
        await server.ConnectAsync(third, "host");
        await server.ReadIdAsync(third);
        await server.RejectedAsync("host", HttpStatusCode.TooManyRequests, "other");
    }

    [Fact]
    public async Task OrdinaryHttpRequestsDoNotConsumeAccountOrGlobalUpgradeAllowances()
    {
        await using var server = new RelayTestServer(options =>
        {
            options.RequestsPerMinute = 3;
            options.RequestsPerMinutePerAccount = 1;
        });
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create("owner"));
        foreach (var _ in Enumerable.Range(0, 3))
        {
            using var response = await client.GetAsync("/relay/v1/host", server.Token);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var control = server.Socket();
        await server.ConnectAsync(control, "host");
        await server.ReadIdAsync(control);
    }

    [Fact]
    public async Task InvalidRoutesDoNotConsumeTheGlobalUpgradeAllowance()
    {
        await using var server = new RelayTestServer(options =>
        {
            options.RequestsPerMinute = 3;
            options.RequestsPerMinutePerAccount = 1;
        });
        await server.RejectedAsync($"sessions/{Guid.NewGuid()}/guest", HttpStatusCode.NotFound);
        await server.RejectedAsync($"sessions/{Guid.NewGuid()}/guest", HttpStatusCode.NotFound);
        await server.RejectedAsync($"sessions/{Guid.NewGuid()}/guest", HttpStatusCode.NotFound);
        using var control = server.Socket();
        await server.ConnectAsync(control, "host");
        await server.ReadIdAsync(control);
    }

    [Theory]
    [InlineData("192.0.2.1", 429)]
    [InlineData("192.0.2.2", 503)]
    public async Task PreauthenticationIpLimitsRejectOrdinaryHttpBeforeUpgradeValidation(string secondAddress, int expected)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Relay:Enabled", "true");
            builder.UseSetting("Relay:RequireAuthentication", "false");
            builder.UseSetting("Relay:RequestsPerMinutePerIp", "1");
            builder.UseSetting("Relay:MaxQuotaEntries", "1");
        });
        using var client = factory.CreateClient();
        await SendAsync("192.0.2.1", 400);
        await SendAsync(secondAddress, expected);

        async Task SendAsync(string address, int status)
        {
            var result = await factory.Server.SendAsync(context =>
            {
                context.Request.Method = "GET";
                context.Request.Path = "/relay/v1/host";
                context.Request.Scheme = "https";
                context.Connection.RemoteIpAddress = IPAddress.Parse(address);
            }, TestContext.Current.CancellationToken);
            Assert.Equal(status, result.Response.StatusCode);
            Assert.False(result.Response.Headers.ContainsKey("Retry-After"));
        }
    }

    private static IHost CreateConfigurationHost(Dictionary<string, string?> settings) =>
        new HostBuilder()
            .UseEnvironment(Environments.Development)
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(settings))
            .ConfigureServices((context, services) => services.AddRelay(context.Configuration))
            .Build();
}
