using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Novalist.Sync.Server.Data;
using Npgsql;

namespace Novalist.Sync.Server.Tests;

public sealed class MigrationUpgradeTests(DatabaseFixture database)
{
    [Fact]
    public async Task CollaborationMigrationPreservesPopulatedReleasedSchema()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Database = $"novalist_upgrade_test_{Guid.NewGuid():N}"
        };
        await using var db = new SyncDbContext(new DbContextOptionsBuilder<SyncDbContext>()
            .UseNpgsql(connection.ConnectionString).UseSnakeCaseNamingConvention().Options);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20261019010739_InitialCreate", ct);
            var user = Guid.NewGuid();
            var device = Guid.NewGuid();
            var project = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO users (id, issuer, subject, created_at) VALUES ({user}, 'https://migration.test', 'writer', {now});
                INSERT INTO devices (id, user_id, created_at) VALUES ({device}, {user}, {now});
                INSERT INTO projects (id, current_epoch, epoch_started_at, last_server_seq, created_at)
                    VALUES ({project}, 0, {now}, 0, {now});
                INSERT INTO memberships (project_id, user_id, role) VALUES ({project}, {user}, 1);
                """, ct);

            await db.Database.MigrateAsync(ct);

            Assert.Equal(2, (await db.Database.GetAppliedMigrationsAsync(ct)).Count());
            var restored = await db.Devices.SingleAsync(ct);
            Assert.Equal(device, restored.Id);
            Assert.Equal(user, restored.UserId);
            Assert.Null(restored.SigningPublicKey);
            Assert.Equal(MembershipRole.Owner, (await db.Memberships.SingleAsync(ct)).Role);
            Assert.Equal(project, (await db.Projects.SingleAsync(ct)).Id);
            var lease = Guid.NewGuid();
            db.FieldLeaseAttempts.Add(new()
            {
                ProjectId = project,
                DeviceId = device,
                LeaseId = lease,
                ExpiresAt = now.AddMinutes(1)
            });
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            Assert.Equal(device, (await db.FieldLeaseAttempts.SingleAsync(ct)).DeviceId);

            await db.GetService<IMigrator>().MigrateAsync("20261019010739_InitialCreate", ct);
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM devices").SingleAsync(ct));
            await db.Database.MigrateAsync(ct);
            Assert.Equal(device, (await db.Devices.SingleAsync(ct)).Id);
            Assert.Empty(await db.FieldLeaseAttempts.ToListAsync(ct));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }
}
