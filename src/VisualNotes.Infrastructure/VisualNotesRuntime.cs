using Microsoft.EntityFrameworkCore;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.Persistence;
using VisualNotes.Infrastructure.Security;

namespace VisualNotes.Infrastructure;

/// <summary>Owns the infrastructure services created for one application process.</summary>
public sealed class VisualNotesRuntime : IAsyncDisposable
{
    private readonly VisualNotesDbContext _database;

    private VisualNotesRuntime(
        VisualNotesDbContext database,
        SessionCoordinator coordinator,
        ISessionRepository sessions,
        IScreenshotRepository screenshots,
        IScreenshotStorageService screenshotStorage,
        ImageFileStore imageFiles,
        IUnitOfWork unitOfWork)
    {
        _database = database;
        Coordinator = coordinator;
        Sessions = sessions;
        Screenshots = screenshots;
        ScreenshotStorage = screenshotStorage;
        ImageFiles = imageFiles;
        UnitOfWork = unitOfWork;
        ApiCredentials = new WindowsDpapiCredentialStore();
    }

    public SessionCoordinator Coordinator { get; }

    public ISessionRepository Sessions { get; }

    public IScreenshotRepository Screenshots { get; }

    public IScreenshotStorageService ScreenshotStorage { get; }

    public ImageFileStore ImageFiles { get; }

    public IUnitOfWork UnitOfWork { get; }

    public IApiCredentialStore ApiCredentials { get; }

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
            var screenshots = new ScreenshotRepository(database);
            var unitOfWork = new UnitOfWork(database);
            var screenshotStorage = new ScreenshotStorageService(dataDirectory);
            var imageFiles = new ImageFileStore(dataDirectory);
            var coordinator = new SessionCoordinator(sessions, screenshots, new SettingsRepository(database), unitOfWork);
            return new VisualNotesRuntime(database, coordinator, sessions, screenshots, screenshotStorage, imageFiles, unitOfWork);
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();
}
