using Microsoft.EntityFrameworkCore;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Data;
using Npgsql;

namespace Novalist.Sync.Server.Access;

public sealed partial class InvitationAccess(SyncDbContext db, ProjectAccess projects, TimeProvider clock)
{
    public async Task CreateAsync(Guid ownerId, Guid projectId, CreateInvitation request, CancellationToken ct, bool legacyAccount = false, Guid? deviceId = null)
    {
        await using var transaction = await AccountAccessLock.AcquireAsync(db, ownerId, legacyAccount, ct);
        await EnsureDeviceAsync(ownerId, deviceId, ct);
        await LockProjectAsync(projectId, ct);
        await projects.EnsureIsOwnerAsync(ownerId, projectId, ct);
        var now = clock.GetUtcNow();
        var expiresAt = new DateTimeOffset(request.ExpiresAt.UtcTicks / 10 * 10, TimeSpan.Zero);
        if (request.InvitationId == Guid.Empty || expiresAt <= now || expiresAt > now.AddDays(7))
            throw new ArgumentException("An invitation needs a random identifier and an expiry within seven days.");

        db.Invitations.Add(new Invitation
        {
            Id = request.InvitationId,
            ProjectId = projectId,
            ExpiresAt = expiresAt
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "pk_invitations" })
        {
            db.ChangeTracker.Clear();
            var previous = await db.Invitations.FindAsync([request.InvitationId], ct);
            if (previous?.ProjectId != projectId || previous.ExpiresAt != expiresAt)
                throw new ConflictException("The invitation identifier is already in use.");
        }
        await transaction.CommitAsync(ct);
    }

    public async Task<InvitationGrant> RedeemAsync(Guid userId, Guid invitationId, CancellationToken ct, bool legacyAccount = false, Guid? deviceId = null)
    {
        await using var transaction = await AccountAccessLock.AcquireAsync(db, userId, legacyAccount, ct);
        await EnsureDeviceAsync(userId, deviceId, ct);
        var projectId = await db.Invitations.AsNoTracking().Where(value => value.Id == invitationId)
            .Select(value => (Guid?)value.ProjectId).SingleOrDefaultAsync(ct)
            ?? throw new AccessDeniedException("This invitation is unavailable.");
        await LockProjectAsync(projectId, ct);
        var rows = await db.Invitations.FromSqlInterpolated(
            $"SELECT * FROM invitations WHERE id = {invitationId} FOR UPDATE").ToListAsync(ct);
        var invitation = rows.SingleOrDefault();
        if (invitation is null || invitation.ExpiresAt <= clock.GetUtcNow() ||
            (invitation.RedeemedBy is { } claimant && claimant != userId))
            throw new AccessDeniedException("This invitation is unavailable.");

        await AdmitAsync(invitation, userId, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new InvitationGrant(invitation.ProjectId);
    }

    private async Task AdmitAsync(Invitation invitation, Guid userId, CancellationToken ct)
    {
        var membership = await db.Memberships.FindAsync([invitation.ProjectId, userId], ct);
        if (invitation.RedeemedBy is not null && membership is null)
            throw new AccessDeniedException("This invitation is unavailable.");
        if (invitation.RedeemedBy is null)
        {
            var contributor = (int)MembershipRole.Contributor;
            var viewer = (int)MembershipRole.Viewer;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO memberships (project_id, user_id, role)
                VALUES ({invitation.ProjectId}, {userId}, {contributor})
                ON CONFLICT (project_id, user_id) DO UPDATE SET role = {contributor}
                WHERE memberships.role = {viewer}
                """, ct);
        }
        invitation.RedeemedBy = userId;
    }

    public async Task RevokeAsync(Guid ownerId, Guid projectId, Guid invitationId, CancellationToken ct, bool legacyAccount = false, Guid? deviceId = null)
    {
        await using var transaction = await AccountAccessLock.AcquireAsync(db, ownerId, legacyAccount, ct);
        await EnsureDeviceAsync(ownerId, deviceId, ct);
        await LockProjectAsync(projectId, ct);
        await projects.EnsureIsOwnerAsync(ownerId, projectId, ct);
        if (invitationId == Guid.Empty) throw new ArgumentException("An invitation identifier is required.");
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO invitations (id, project_id, expires_at)
            VALUES ({invitationId}, {projectId}, {DateTimeOffset.UnixEpoch})
            ON CONFLICT (id) DO UPDATE SET expires_at = {DateTimeOffset.UnixEpoch}
            WHERE invitations.project_id = {projectId}
            """, ct);
        if (affected == 0) throw new ConflictException("The invitation belongs to a different project.");
        await transaction.CommitAsync(ct);
    }

    private async Task EnsureDeviceAsync(Guid userId, Guid? deviceId, CancellationToken ct)
    {
        if (deviceId is { } id && !await DeviceRegistry.ActiveDevices(db).AnyAsync(device => device.Id == id && device.UserId == userId, ct))
            throw new AccessDeniedException("The device is no longer active for this account.");
    }

    private Task<List<Project>> LockProjectAsync(Guid projectId, CancellationToken ct) =>
        db.Projects.FromSqlInterpolated($"SELECT * FROM projects WHERE id = {projectId} FOR UPDATE").ToListAsync(ct);
}
