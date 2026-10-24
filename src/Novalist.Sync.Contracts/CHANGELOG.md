# Changelog

All notable changes to `Novalist.Sync.Contracts` are documented here. The
format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
the package uses [Semantic Versioning](https://semver.org/): breaking wire
changes bump `Protocol.Version` and the major version, additive changes bump
the minor version.

## [Unreleased]

### Added

- `RelayProtocol` publishes the connection-renewal header and endpoint. Renewed
  access tokens update existing relay connections while preserving session IDs.
- `DeviceEnrollmentRequest`, `DeviceEnrollmentChallenge`, `DeviceEnrollmentProof`
  and `Protocol.DeviceEnrollmentPath` support fresh browser MFA with a signed
  ID-token nonce bound to ordinary and recovery public keys. API access tokens
  can remain ordinary bearer tokens.

- `DeviceRecoveryRegistration` and `VerifiedDeviceRegistration.RecoveryDevice`
  provision a public recovery identity atomically with the first verified device.
  First enrollment requires recent MFA bound through the one-use browser
  challenge or a provider-issued `cnf.jkt`; existing device retries and signed links retain their
  authentication flow. Only public keys and proofs cross the API.

- `ReserveProjectEpoch` freezes the update boundary before checkpoint creation;
  `CancelProjectEpoch` releases an abandoned reservation. Reservations expire
  after two minutes. Confirmed update retries retain their original sequence.
- `FieldLeaseRequest.AcquireBefore` bounds acquisition retries. New acquisitions
  must supply a fixed deadline no more than two minutes ahead and reuse it on
  retries. `GetFieldLeaseTime` supplies the database clock for those deadlines,
  avoiding dependence on the device clock. Renewals, edits, and release of an
  existing lease can omit the deadline; cancellation before acquisition requires
  the original deadline to reject delayed acquisition. Older clients
  without a deadline receive an explicit rejection. Package minor 1.8.0.

- `NativeClientConfiguration.AuthenticationRequired` advertises optional relay
  authentication; omitted values require sign-in for compatibility. Anonymous
  relay configurations omit the identity-provider settings: `Authority`, `ClientId`
  and `Scope` are now nullable (`string?`), and consumers must handle null values.
  Package minor 1.7.0.

- Stable string role names in `ProjectRoles` (`Owner`, `Contributor`, `Viewer`),
  retaining the existing JSON and MessagePack representation of `ProjectMember.Role`.
- A dedicated `RevokeInvitation` request and shared `Protocol.DeviceRegistrationPath`
  and `Protocol.VerifiedDevicesPath` constants for verified-device endpoints.

- `PushGuardedFieldEdit` atomically requires both the focused field lease and a
  second opaque resource lease in the same project and epoch. Expired guards
  cannot commit delayed edits; acknowledged retries keep their original sequence.
  The server learns which two opaque resources participate in the same change.
  Package minor 1.6.0; earlier servers fail closed because the method is absent.

- Verified-device certificates and staged project epoch transitions. `BeginProjectEpoch`,
  `StageProjectEpoch`, `StageProjectEpochGrant`, and `CommitProjectEpoch` publish a
  fresh encrypted checkpoint, remove membership and retire invitations atomically,
  comparing both the previous epoch and log sequence. `GetProjectEpochGrant` returns
  only the requesting remaining device's opaque grant. Clients verify owner signatures.
  Key grants are streamed individually without a participant cap. Package minor 1.6.0.
  `ISyncClient.ProjectEpochChanged(projectId, epoch)` notifies remaining members
  after the checkpoint and access change commit.

- Owner-only verified-device project access management: `ListProjectMembers`,
  `RemoveProjectMember`, `DeleteProject`, and `ProjectAccessEnded` notifications.
  Removal expires unused links and releases the account's field leases. Deletion
  removes project data while retaining its identifier as an unavailable tombstone.

- Exclusive field leases: acquire, renew, release and atomically commit an
  encrypted field edit. Grants include a catch-up sequence and server time;
  resource IDs are opaque and no field names or values enter the protocol.
- `ActivateInvitation` binds a single-use claim to its verified device and a
  32-byte opaque certificate fingerprint. `InvitationActivation` can be retrieved
  by that same active account/device/certificate after expiry, allowing recovery
  when a successful claim response is lost. It cannot restore removed membership.
- Device possession proofs: `DeviceRegistrationContext`, `DevicePublicKeys`,
  `VerifiedDeviceRegistration`, `DeviceConnectionChallenge` and fixed big-endian
  `DeviceProof` encodings. `/devices/registration` returns the signed-in account
  ID; `/devices/verified` accepts immutable public keys and registration/linking
  signatures. `GetDeviceChallenge` and `AuthenticateDevice` authenticate a hub
  connection. Accounts opting into verified devices cannot use legacy devices.
- Hub invitation creation, redemption and revocation for verified connections.
  Legacy invitation HTTP endpoints reject accounts that have verified devices.
- `NativeClientConfiguration` and `Protocol.ClientConfigurationPath` expose the
  protocol version and public OIDC native-client settings at
  `GET /client-configuration`, without authentication.
- Authenticated, owner-created single-use invitations: `CreateInvitation`,
  `RedeemInvitation` and `InvitationGrant` records. Creation and revocation
  use `/projects/{projectId}/invitations` and its `/revoke` endpoint;
  claiming uses `/invitations/redeem`. All three are POST requests.
- `ISyncHub.PublishPresence` and `ISyncClient.ReceivePresence` relay encrypted
  presence envelopes without storing them or allocating a server sequence.

- `Protocol.Version` (protocol version 1).
- `MsgType` enum: `Update = 1`, `Snapshot = 2`, `Presence = 3`.
- `EnvelopeHeader` record with the 62-byte layout from design.md §7:
  `WriteTo`, `ToBytes` and `Parse` (big-endian integers, RFC 9562 UUID byte
  order).
- `UnsupportedProtocolVersionException`, thrown by `EnvelopeHeader.Parse` for
  an unknown `protoVersion`.
- `Envelope` record: raw header bytes, nonce, ciphertext and signature, all
  opaque `byte[]`.
- `SequencedEnvelope` record: an `Envelope` with the `ServerSeq` the server
  assigned.
- `Protocol.HubPath` (`/sync`), the path of the SignalR hub.
- `Protocol.DevicesPath` (`/devices`): devices register with
  `PUT /devices/{deviceId}` before connecting to the hub.
- `Protocol.DeviceIdParameter` (`deviceId`): the query parameter that names
  the connecting device, as in `/sync?deviceId=<guid>`.
- `ISyncHub` (client → server): `CreateProject` (the caller becomes owner),
  `JoinProject` / `LeaveProject` (start / stop receiving live updates of a
  project), `Push` (returns the assigned `serverSeq`) and `CatchUp` (streams
  every update after `afterServerSeq`).
- `ISyncClient` (server → client): `ReceiveUpdate`, `ReceivePresence`,
  `ProjectAccessEnded` and `ProjectEpochChanged`.
