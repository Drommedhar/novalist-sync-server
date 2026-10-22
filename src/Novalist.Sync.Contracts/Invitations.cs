namespace Novalist.Sync.Contracts;

public sealed record CreateInvitation(Guid InvitationId, DateTimeOffset ExpiresAt);
public sealed record RedeemInvitation(Guid InvitationId);
public sealed record InvitationGrant(Guid ProjectId);
