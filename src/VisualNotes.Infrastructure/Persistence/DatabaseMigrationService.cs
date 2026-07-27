using Microsoft.EntityFrameworkCore;

namespace VisualNotes.Infrastructure.Persistence;

public sealed class DatabaseMigrationService(VisualNotesDbContext db)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try { await db.Database.MigrateAsync(cancellationToken); }
        finally { Gate.Release(); }
    }
}
