# Collaboration migrations

Each stacked change adds only the schema it uses: verified device keys,
invitations, epoch rotation, then field leases. Relay, membership management and
presence need no new tables. Migrations extend the original `InitialCreate`
schema; regression tests preserve populated users, devices, memberships and
updates through upgrade, rollback and reapplication.

Back up persistent databases before upgrading. Development databases that ran
the earlier combined `CollaborationAccess` migration or intermediate unreleased
migrations need recreation if disposable, or a separate data-preserving
reconciliation. Do not apply the stack over that unrelated migration history.
Relay deployments need no database or migrations.

`VerifiedDevices` adds unique signing/receiving public-key indexes and a foreign
key from each linked device to its authorizing device. Existing legacy records
have null keys and are preserved. Rolling it back discards verified key and link
metadata; restore a backup before resuming verified-device access after rollback.
