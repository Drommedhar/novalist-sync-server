using Novalist.Sync.Contracts;

namespace Novalist.Sync.Server.Hubs;

public sealed partial class SyncHub
{
    public async Task CreateInvitation(Guid projectId, CreateInvitation request)
    {
        await using var access = await devices.PauseAccessChangesAsync(UserId, Context.ConnectionAborted);
        await EnsureDeviceActiveAsync(Context.ConnectionAborted);
        await invitations.CreateAsync(UserId, projectId, request, Context.ConnectionAborted, LegacyConnection, DeviceId);
    }

    public async Task<InvitationGrant> RedeemInvitation(Guid invitationId)
    {
        await using var access = await devices.PauseAccessChangesAsync(UserId, Context.ConnectionAborted);
        await EnsureDeviceActiveAsync(Context.ConnectionAborted);
        return await invitations.RedeemAsync(UserId, invitationId, Context.ConnectionAborted, LegacyConnection, DeviceId);
    }

    public async Task RevokeInvitation(Guid projectId, Guid invitationId)
    {
        await using var access = await devices.PauseAccessChangesAsync(UserId, Context.ConnectionAborted);
        await EnsureDeviceActiveAsync(Context.ConnectionAborted);
        await invitations.RevokeAsync(UserId, projectId, invitationId, Context.ConnectionAborted, LegacyConnection, DeviceId);
    }

    public async Task<InvitationActivation> ActivateInvitation(ActivateInvitation request)
    {
        await using var access = await devices.PauseAccessChangesAsync(UserId, Context.ConnectionAborted);
        await EnsureDeviceActiveAsync(Context.ConnectionAborted);
        return await invitations.ActivateAsync(UserId, DeviceId, request, Context.ConnectionAborted);
    }

    private bool LegacyConnection => !Context.Items.TryGetValue(AuthenticatedItem, out var authenticated) || authenticated is not true;
}
