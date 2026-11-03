using System.Security.Claims;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Hubs;

namespace Novalist.Sync.Server.Access;

public sealed class VerifiedDeviceEnrollment(UserDirectory users, DeviceRegistry devices,
    ConnectionRegistry connections, DeviceEnrollmentPolicy policy, DeviceEnrollmentChallenges challenges, DeviceEnrollmentTokens tokens)
{
    public async Task RegisterAsync(ClaimsPrincipal principal, VerifiedDeviceRegistration request, string serverUrl, CancellationToken ct)
    {
        var user = await users.GetOrCreateAsync(principal, ct);
        using var claim = request.EnrollmentProof is null ? null : challenges.Claim(user.Id, serverUrl, request);
        if (request.EnrollmentProof is { } proof && claim is { } claimed)
            await tokens.ValidateAsync(principal, claimed.Challenge, proof.IdentityToken, ct);
        var change = await devices.RegisterVerifiedAsync(user.Id, request, serverUrl, keys =>
        {
            if (claim is null) policy.Authorize(principal, keys);
            else claim.Validate();
        }, ct);
        claim?.Complete();
        if (change is DeviceRegistrationChange.FirstVerified or DeviceRegistrationChange.Unchanged) connections.AbortUnverifiedUser(user.Id);
        else if (change == DeviceRegistrationChange.Linked) connections.AbortDevice(request.Keys.DeviceId);
    }
}
