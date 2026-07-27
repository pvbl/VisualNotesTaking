using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using VisualNotes.Core.Models;
using VisualNotes.Infrastructure.Persistence;
using VisualNotes.Infrastructure.Diagnostics;
using VisualNotes.Testing.Infrastructure;
using Xunit;

namespace VisualNotes.IntegrationTests;

public sealed class BackupSecretLeakTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Backup_and_diagnostic_payloads_exclude_known_test_secrets()
    {
        const string secret = "backup-leak-canary-secret";
        await using var factory = new TemporarySqliteFactory();
        await using var db = await factory.CreateContextAsync();
        var session = new NoteSession { Name = "Safe session" };
        db.Sessions.Add(session);
        db.Settings.Add(new AppSetting { Key = "legacy-secret", JsonValue = $"\"{secret}\"", IsSecret = true });
        await db.SaveChangesAsync();
        await using var archive = new MemoryStream();
        await new SessionBackupService(db, factory.RootPath).CreateAsync(session.Id, archive);

        AssertArchiveDoesNotContain(archive, secret);

        await using var diagnostics = new MemoryStream();
        await new DiagnosticPackageService().CreateAsync(diagnostics);
        AssertArchiveDoesNotContain(diagnostics, secret);
    }

    private static void AssertArchiveDoesNotContain(MemoryStream archive, string secret)
    {
        archive.Position = 0;
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            reader.ReadToEnd().ShouldNotContain(secret, $"secret leaked through {entry.FullName}");
        }
    }
}
