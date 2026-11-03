using Microsoft.EntityFrameworkCore;

namespace Novalist.Sync.Server.Tests.Infrastructure;

internal static class DatabaseLocks
{
    public static async Task WaitUntilBlockedAsync(DatabaseFixture database, int processId, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await using var db = database.CreateContext();
        try
        {
            while (await db.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM pg_stat_activity
                WHERE pid = {processId} AND wait_event_type = 'Lock'
                """).SingleAsync(timeout.Token) == 0)
                await Task.Delay(10, timeout.Token);
        }
        catch (OperationCanceledException error) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Database process {processId} did not block on a lock within five seconds.", error);
        }
    }
}
