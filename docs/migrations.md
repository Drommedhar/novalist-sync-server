# Collaboration database upgrade

Relay mode needs no database or migration. Persistent mode upgrades the released
`20261019010739_InitialCreate` schema with one `CollaborationAccess` migration.
The ten intermediate migrations from this pull request have been squashed before
release. The original migration and existing user, device, membership and update
records remain intact.

The field-attempt table is created with its device foreign key and expiry column
already present. There is no intermediate empty-GUID default that could invalidate
existing rows. The upgrade regression starts with populated released tables,
applies the migration, uses the new device-scoped table and checks rollback and
reapplication in an isolated test database.

Back up an existing persistent database before applying migrations. A development
database that ran the intermediate, unreleased migrations has a different migration
history: recreate disposable test data or prepare a separate data-preserving
upgrade before using this squashed migration. Do not mark the new migration as
applied without reconciling its schema. This change does not migrate any deployment.
