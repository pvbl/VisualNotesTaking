using Microsoft.EntityFrameworkCore;

using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.Documents;
using VisualNotes.Infrastructure.Persistence;
using VisualNotes.Testing.Infrastructure;

namespace VisualNotes.IntegrationTests;

/// <summary>Crash-boundary tests: an injected failure must yield the old valid state or the new valid state, never silent corruption.</summary>
public sealed class FaultInjectionTests
{
    [Fact]
    public async Task Interrupted_backup_write_keeps_previous_valid_backup()
    {
        await using var factory = new TemporarySqliteFactory();
        await using var db = await factory.CreateContextAsync();
        var session = new NoteSession(); db.Sessions.Add(session); await db.SaveChangesAsync();
        var path = Path.Combine(factory.RootPath, "automatic.vnotes");
        var service = new SessionBackupService(db, factory.RootPath);
        await service.CreateFileAsync(session.Id, path);
        var original = await File.ReadAllBytesAsync(path);

        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => service.CreateFileAsync(session.Id, path, cancelled.Token));
        (await File.ReadAllBytesAsync(path)).ShouldBe(original);
        await using var backup = File.OpenRead(path);
        await SessionBackupService.ValidateAsync(backup);
    }

    [Fact]
    public async Task Database_failure_rolls_back_the_entire_incremental_save()
    {
        await using var factory = new TemporarySqliteFactory();
        await using var db = await factory.CreateContextAsync();
        var original = new NoteSession { Name = "before" }; db.Sessions.Add(original); await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        original.Name = "after";
        db.Settings.AddRange(new AppSetting { Key = "duplicate" }, new AppSetting { Key = "duplicate" }); // injected unique-key failure
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        await transaction.RollbackAsync();
        db.ChangeTracker.Clear();
        (await db.Sessions.SingleAsync()).Name.ShouldBe("before");
    }

    [Fact]
    public async Task Interrupted_export_does_not_replace_the_last_good_document()
    {
        var path = Path.Combine(Path.GetTempPath(), $"visualnotes-fault-{Guid.NewGuid():N}.docx");
        try
        {
            var document = new SemanticDocument("recoverable", [new("section", SemanticNodeType.Section,
                SemanticContentOrigin.Observed, "content")]);
            var exporter = new OpenXmlDocumentExporter();
            await exporter.ExportAsync(new(document, path, new()));
            var original = await File.ReadAllBytesAsync(path);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => exporter.ExportAsync(new(document, path, new(),
                ExpectedExistingVersion: ExportFileVersion.Read(path)), cancelled.Token));
            (await File.ReadAllBytesAsync(path)).ShouldBe(original);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".visualnotes-export.json")) File.Delete(path + ".visualnotes-export.json");
        }
    }
}
