using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Access;

namespace Novalist.Sync.Server.Hubs;

public sealed partial class SyncHub
{
    public async Task CreateInvitation(Guid projectId, CreateInvitation request)
    {
        await using var access = await devices.PauseAccessChangesAsync(UserId, Context.ConnectionAborted);
        await RequireInvitationDeviceAsync();
        await invitations.CreateAsync(UserId, projectId, request, Context.ConnectionAborted, deviceId: DeviceId);
    }

    public async Task<InvitationGrant> RedeemInvitation(Guid invitationId)
    {
        await using var access = await devices.PauseAccessChangesAsync(UserId, Context.ConnectionAborted);
        await RequireInvitationDeviceAsync();
        return await invitations.RedeemAsync(UserId, invitationId, Context.ConnectionAborted, deviceId: DeviceId);
    }

    public async Task RevokeInvitation(Guid projectId, Guid invitationId)
    {
        await using var access = await devices.PauseAccessChangesAsync(UserId, Context.ConnectionAborted);
        await RequireInvitationDeviceAsync();
        await invitations.RevokeAsync(UserId, projectId, invitationId, Context.ConnectionAborted, deviceId: DeviceId);
    }

    public async Task<InvitationActivation> ActivateInvitation(ActivateInvitation request)
    {
        await using var access = await devices.PauseAccessChangesAsync(UserId, Context.ConnectionAborted);
        await RequireInvitationDeviceAsync();
        return await invitations.ActivateAsync(UserId, DeviceId, request, Context.ConnectionAborted);
    }

    private async Task RequireInvitationDeviceAsync()
    {
        await EnsureDeviceActiveAsync(Context.ConnectionAborted);
        if (!Context.Items.TryGetValue(AuthenticatedItem, out var authenticated) || authenticated is not true)
            throw new AccessDeniedException("Hub invitations require a verified device connection.");
    }
}
