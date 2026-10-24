namespace Novalist.Sync.Contracts;

public sealed record FieldLeaseRequest(Guid ProjectId, Guid ResourceId, uint Epoch, Guid LeaseId, DateTimeOffset? AcquireBefore = null);
public sealed record FieldLeaseGrant(Guid DeviceId, Guid LeaseId, DateTimeOffset ExpiresAt, DateTimeOffset ServerTime, long ServerSeq);
