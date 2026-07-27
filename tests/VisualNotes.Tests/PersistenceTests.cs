using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VisualNotes.Core.Models;
using VisualNotes.Infrastructure.Persistence;

namespace VisualNotes.Tests;

public sealed class PersistenceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "visualnotes-tests-" + Guid.NewGuid().ToString("N"));
    private VisualNotesDbContext _db = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _db = new VisualNotesDbContext(new DbContextOptionsBuilder<VisualNotesDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "test.db")}").Options);
        await new DatabaseMigrationService(_db).MigrateAsync();
    }

    public async Task DisposeAsync() { await _db.DisposeAsync(); Directory.Delete(_root, true); }

    [Fact]
    public async Task Repository_round_trips_session_and_image_metadata_without_blob()
    {
        var session = new NoteSession { Name = "Álgebra" };
        var shot = new Screenshot { SessionId = session.Id, Width = 1920, Height = 1080, PerceptualHash = "abc", Image = new ScreenshotImage { RelativePath = "sessions/a/captures/b.png", ByteLength = 42, Sha256 = "def" } };
        session.Screenshots.Add(shot);
        await new SessionRepository(_db).AddAsync(session);
        await new UnitOfWork(_db).SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var loaded = await new SessionRepository(_db).GetAsync(session.Id);
        Assert.Equal("sessions/a/captures/b.png", Assert.Single(loaded!.Screenshots).Image!.RelativePath);
        var columns = await _db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('ScreenshotImages')").ToListAsync();
        Assert.DoesNotContain(columns, x => x.Contains("Data", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Backup_contains_database_and_files_but_removes_secrets()
    {
        var session = new NoteSession();
        _db.Add(session);
        _db.Add(new AppSetting { Key = "api-key", JsonValue = "super-secret", IsSecret = true });
        _db.Add(new ProviderProfile { Name = "provider", ApiKeyReference = "secret-store:key" });
        await _db.SaveChangesAsync();
        var folder = Path.Combine(_root, "sessions", session.Id.ToString("N"), "captures");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "capture.png"), "image");

        await using var output = new MemoryStream();
        await new SessionBackupService(_db, _root).CreateAsync(session.Id, output);
        output.Position = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("manifest.json"));
        Assert.NotNull(zip.GetEntry("files/captures/capture.png"));
        var dbEntry = zip.GetEntry("database/visualnotes.db")!;
        var restored = Path.Combine(_root, "restored.db");
        await using (var input = dbEntry.Open()) await using (var file = File.Create(restored)) await input.CopyToAsync(file);
        await using var connection = new SqliteConnection($"Data Source={restored}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM Settings WHERE IsSecret=1) + (SELECT COUNT(*) FROM ProviderProfiles WHERE ApiKeyReference IS NOT NULL)";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }
}
