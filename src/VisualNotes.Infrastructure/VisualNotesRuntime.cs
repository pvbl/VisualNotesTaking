using Microsoft.EntityFrameworkCore;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.Persistence;
using VisualNotes.Infrastructure.Security;
using VisualNotes.Infrastructure.Documents;
using VisualNotes.Infrastructure.Processing;

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
        IUnitOfWork unitOfWork, ICaptureWorkspace captureWorkspace, IDocumentExporter documentExporter,
        ISettingsRepository settings, DurableAnalysisJobProcessor analysisJobs, IExtractionArtifactStore artifacts)
    {
        _database = database;
        Coordinator = coordinator;
        Sessions = sessions;
        Screenshots = screenshots;
        ScreenshotStorage = screenshotStorage;
        ImageFiles = imageFiles;
        UnitOfWork = unitOfWork;
        CaptureWorkspace = captureWorkspace;
        DocumentExporter = documentExporter;
        ApiCredentials = new WindowsDpapiCredentialStore();
        Settings = settings;
        AnalysisJobs = analysisJobs;
        ExtractionArtifacts = artifacts;
    }

    public SessionCoordinator Coordinator { get; }

    public ISessionRepository Sessions { get; }

    public IScreenshotRepository Screenshots { get; }

    public IScreenshotStorageService ScreenshotStorage { get; }

    public ImageFileStore ImageFiles { get; }

    public IUnitOfWork UnitOfWork { get; }

    public IApiCredentialStore ApiCredentials { get; }
    public ICaptureWorkspace CaptureWorkspace { get; }
    public IDocumentExporter DocumentExporter { get; }
    public ISettingsRepository Settings { get; }
    public DurableAnalysisJobProcessor AnalysisJobs { get; }
    public IExtractionArtifactStore ExtractionArtifacts { get; }

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
            var settings = new SettingsRepository(database);
            var coordinator = new SessionCoordinator(sessions, screenshots, settings, unitOfWork);
            var factory = new RuntimeDbContextFactory(database.Database.GetDbConnection().ConnectionString);
            var artifacts = new FileExtractionArtifactStore(dataDirectory);
            var credentials = new WindowsDpapiCredentialStore();
            var handler = new VisualAnalysisJobHandler(factory, dataDirectory, credentials,
                new SettingsImageTransmissionConsent(settings), artifacts);
            var jobs = new DurableAnalysisJobProcessor(factory, handler);
            return new VisualNotesRuntime(database, coordinator, sessions, screenshots, screenshotStorage, imageFiles, unitOfWork,
                new CaptureWorkspace(database, screenshots), new OpenXmlDocumentExporter(), settings, jobs, artifacts);
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async Task<int> RecoverIncompleteJobsAsync(CancellationToken cancellationToken = default) =>
        await AnalysisJobs.DrainAsync(Enum.GetValues<VisualNotes.Core.Models.AnalysisJobTrigger>(), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await AnalysisJobs.DisposeAsync();
        await _database.DisposeAsync();
    }

    private sealed class RuntimeDbContextFactory(string connectionString) : IDbContextFactory<VisualNotesDbContext>
    {
        public VisualNotesDbContext CreateDbContext() => new(new DbContextOptionsBuilder<VisualNotesDbContext>().UseSqlite(connectionString).Options);
    }
}
