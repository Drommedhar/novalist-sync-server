using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Access;

namespace Novalist.Sync.Server.Hubs;

public sealed partial class SyncHub
{
    public async Task<IReadOnlyList<ProjectMember>> ListProjectMembers(Guid projectId)
    {
        await using var access = await devices.PauseAccessChangesAsync(UserId, Context.ConnectionAborted);
        await RequireVerifiedAccessAsync();
        return await projects.ListMembersAsync(UserId, projectId, Context.ConnectionAborted);
    }

    public async Task RemoveProjectMember(Guid projectId, Guid memberId)
    {
        await using var access = await devices.PauseAccessChangesAsync(UserId, Context.ConnectionAborted);
        await RequireVerifiedAccessAsync();
        await revocation.RemoveMemberAsync(UserId, projectId, memberId, Context.ConnectionAborted);
    }

    public async Task DeleteProject(Guid projectId)
    {
        await using var access = await devices.PauseAccessChangesAsync(UserId, Context.ConnectionAborted);
        await RequireVerifiedAccessAsync();
        await revocation.DeleteProjectAsync(UserId, projectId, Context.ConnectionAborted);
    }

    private async Task RequireVerifiedAccessAsync()
    {
        if (!await EnsureDeviceActiveAsync(Context.ConnectionAborted))
            throw new AccessDeniedException("Managing project access requires a verified device.");
    }
}
