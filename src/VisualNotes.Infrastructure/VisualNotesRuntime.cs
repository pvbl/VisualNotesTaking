using Microsoft.EntityFrameworkCore;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.Persistence;

namespace VisualNotes.Infrastructure;

/// <summary>Owns the infrastructure services created for one application process.</summary>
public sealed class VisualNotesRuntime : IAsyncDisposable
{
    private readonly VisualNotesDbContext _database;

    private VisualNotesRuntime(
        VisualNotesDbContext database,
        SessionCoordinator coordinator,
        ISessionRepository sessions)
    {
        _database = database;
        Coordinator = coordinator;
        Sessions = sessions;
    }

    public SessionCoordinator Coordinator { get; }

    public ISessionRepository Sessions { get; }

    public static async Task<VisualNotesRuntime> CreateAsync(
        string dataDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(dataDirectory);
        var database = new VisualNotesDbContext(new DbContextOptionsBuilder<VisualNotesDbContext>()
            .UseSqlite($"Data Source={Path.Combine(dataDirectory, "visualnotes.db")}")
            .Options);

        try
        {
            await new DatabaseMigrationService(database).MigrateAsync(cancellationToken);
            var sessions = new SessionRepository(database);
            var coordinator = new SessionCoordinator(
                sessions,
                new ScreenshotRepository(database),
                new SettingsRepository(database),
                new UnitOfWork(database));
            return new VisualNotesRuntime(database, coordinator, sessions);
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();
}
