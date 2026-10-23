using Microsoft.EntityFrameworkCore;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Access;
using Novalist.Sync.Server.Data;

namespace Novalist.Sync.Server.Updates;

public sealed class PresenceAccess(SyncDbContext db, DeviceRegistry devices, ProjectAccess projects)
{
    public async Task DeliverAsync(Guid userId, Guid deviceId, Envelope envelope,
        Func<Guid, IReadOnlySet<Guid>, Task> deliver, CancellationToken ct)
    {
        var header = ValidateEnvelope(deviceId, envelope);
        await devices.EnsureActiveAsync(userId, deviceId, ct);
        await projects.EnsureCanReadAsync(userId, header.ProjectId, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Projects.FromSqlInterpolated(
            $"SELECT * FROM projects WHERE id = {header.ProjectId} FOR SHARE").AsNoTracking().ToListAsync(ct);
        // Recheck under the lock and hold it through delivery so rotation cannot overtake the send.
        await devices.EnsureActiveAsync(userId, deviceId, ct);
        await projects.EnsureCanReadAsync(userId, header.ProjectId, ct);
        if (locked.SingleOrDefault()?.CurrentEpoch != header.Epoch)
            throw new PushRejectedException("The presence epoch is no longer current.");
        var recipients = await db.Devices.FromSqlInterpolated($"""
            SELECT devices.* FROM devices
            JOIN memberships ON memberships.user_id = devices.user_id
            WHERE memberships.project_id = {header.ProjectId} AND devices.revoked_at IS NULL
            FOR SHARE OF devices
            """).AsNoTracking().ToListAsync(ct);
        if (!recipients.Any(device => device.Id == deviceId))
            throw new AccessDeniedException("The sending device is no longer active.");
        await deliver(header.ProjectId, recipients.Select(device => device.Id).ToHashSet());
        await transaction.CommitAsync(ct);
    }

    private static EnvelopeHeader ValidateEnvelope(Guid deviceId, Envelope envelope)
    {
        var header = EnvelopeHeader.Parse(envelope.Header);
        if (header.MsgType != MsgType.Presence || header.SenderDeviceId != deviceId ||
            header.SenderSeq is 0 or > long.MaxValue || envelope.Nonce is not { Length: 24 } ||
            envelope.Signature is not { Length: 64 } || envelope.Ciphertext is not { Length: >= 16 and <= 4096 })
            throw new PushRejectedException("Invalid presence envelope.");
        return header;
    }
}
