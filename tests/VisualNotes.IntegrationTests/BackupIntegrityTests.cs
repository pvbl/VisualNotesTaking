using System.IO.Compression;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Infrastructure.Persistence;
using VisualNotes.Testing.Infrastructure;

namespace VisualNotes.IntegrationTests;

public sealed class BackupIntegrityTests
{
    [Fact]
    public async Task Current_backup_validates_and_restores_database_and_files()
    {
        await using var factory = new TemporarySqliteFactory();
        await using var db = await factory.CreateContextAsync();
        var session = new NoteSession { Name = "recoverable" };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        var sessionDirectory = Path.Combine(factory.RootPath, "sessions", session.Id.ToString("N"), "analysis");
        Directory.CreateDirectory(sessionDirectory);
        await File.WriteAllTextAsync(Path.Combine(sessionDirectory, "partial-work.json"), "checkpoint");
        var backup = Path.Combine(factory.RootPath, "manual.vnotes");
        await new SessionBackupService(db, factory.RootPath).CreateFileAsync(session.Id, backup);

        await using (var stream = File.OpenRead(backup))
            (await SessionBackupService.ValidateAsync(stream)).Manifest.Version.ShouldBe(SessionBackupService.FormatVersion);
        var restore = Path.Combine(factory.RootPath, "restored");
        await SessionBackupService.RestoreAsync(backup, restore);
        File.Exists(Path.Combine(restore, "visualnotes.db")).ShouldBeTrue();
        (await File.ReadAllTextAsync(Path.Combine(restore, "sessions", session.Id.ToString("N"), "analysis", "partial-work.json"))).ShouldBe("checkpoint");
    }

    [Fact]
    public async Task A_mutated_payload_is_rejected_instead_of_silently_restored()
    {
        var sessionId = Guid.NewGuid();
        await using var archiveBytes = new MemoryStream();
        using (var zip = new ZipArchive(archiveBytes, ZipArchiveMode.Create, true))
        {
            var database = zip.CreateEntry("database/visualnotes.db");
            await using (var writer = new StreamWriter(database.Open())) await writer.WriteAsync("not a database");
            var manifest = zip.CreateEntry("manifest.json");
            await using var stream = manifest.Open();
            await JsonSerializer.SerializeAsync(stream, new BackupManifest("visualnotes-session", 2, sessionId,
                DateTimeOffset.UtcNow, [new("database/visualnotes.db", 14, new string('0', 64))]));
        }
        archiveBytes.Position = 0;
        await Should.ThrowAsync<InvalidDataException>(() => SessionBackupService.ValidateAsync(archiveBytes));
    }

    [Fact]
    public async Task Version_one_backups_remain_compatible_but_still_get_database_integrity_checks()
    {
        await using var factory = new TemporarySqliteFactory();
        await using var db = await factory.CreateContextAsync();
        db.Sessions.Add(new NoteSession());
        await db.SaveChangesAsync();
        var databasePath = db.Database.GetDbConnection().DataSource;
        await using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            zip.CreateEntryFromFile(databasePath, "database/visualnotes.db");
            var manifest = zip.CreateEntry("manifest.json");
            await using var output = manifest.Open();
            await JsonSerializer.SerializeAsync(output, new BackupManifest("visualnotes-session", 1, Guid.NewGuid(), DateTimeOffset.UtcNow));
        }
        bytes.Position = 0;
        (await SessionBackupService.ValidateAsync(bytes)).Manifest.Version.ShouldBe(1);
    }
}
