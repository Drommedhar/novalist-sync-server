using Microsoft.EntityFrameworkCore;
using Novalist.Sync.Server.Data;

namespace Novalist.Sync.Server.Access;

public sealed class EphemeralStateCleanup(SyncDbContext db)
{
    public async Task PruneAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.FieldLeaseAttempts.Where(attempt => attempt.ExpiresAt <= now).ExecuteDeleteAsync(ct);
        await db.ProjectEpochStages.Where(stage => stage.CreatedAt <= now - ProjectEpochAccess.ReservationLifetime).ExecuteDeleteAsync(ct);
    }
}

public sealed class EphemeralStateCleanupService(IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<EphemeralStateCleanup>().PruneAsync(stoppingToken);
        }
    }
}
