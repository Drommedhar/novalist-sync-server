using System.Security.Claims;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Hubs;

namespace Novalist.Sync.Server.Access;

public sealed class VerifiedDeviceEnrollment(UserDirectory users, DeviceRegistry devices,
    ConnectionRegistry connections, DeviceEnrollmentPolicy policy)
{
    public async Task RegisterAsync(ClaimsPrincipal principal, VerifiedDeviceRegistration request, string serverUrl, CancellationToken ct)
    {
        var user = await users.GetOrCreateAsync(principal, ct);
        var change = await devices.RegisterVerifiedAsync(user.Id, request, serverUrl, keys => policy.Authorize(principal, keys), ct);
        if (change == DeviceRegistrationChange.FirstVerified) connections.AbortUser(user.Id);
        else if (change == DeviceRegistrationChange.Linked) connections.AbortDevice(request.Keys.DeviceId);
    }
}
