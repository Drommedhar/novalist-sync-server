using Microsoft.EntityFrameworkCore;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Access;
using Novalist.Sync.Server.Data;

namespace Novalist.Sync.Server.Updates;

public sealed class PresenceAccess(SyncDbContext db, DeviceRegistry devices, ProjectAccess projects)
{
    public async Task<Guid> ValidateAsync(Guid userId, Guid deviceId, Envelope envelope, CancellationToken ct)
    {
        var header = EnvelopeHeader.Parse(envelope.Header);
        if (header.MsgType != MsgType.Presence || header.SenderDeviceId != deviceId ||
            header.SenderSeq is 0 or > long.MaxValue || envelope.Nonce is not { Length: 24 } ||
            envelope.Signature is not { Length: 64 } || envelope.Ciphertext is not { Length: >= 16 and <= 4096 })
            throw new PushRejectedException("Invalid presence envelope.");
        await devices.EnsureActiveAsync(userId, deviceId, ct);
        await projects.EnsureCanReadAsync(userId, header.ProjectId, ct);
        var epoch = await db.Projects.Where(p => p.Id == header.ProjectId).Select(p => p.CurrentEpoch).SingleAsync(ct);
        if (epoch != header.Epoch) throw new PushRejectedException("The presence epoch is no longer current.");
        return header.ProjectId;
    }
}
