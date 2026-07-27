using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace VisualNotes.Infrastructure.Persistence;

public sealed class SessionBackupService(VisualNotesDbContext db, string dataDirectory)
{
    public const int FormatVersion = 1;

    public async Task CreateAsync(Guid sessionId, Stream destination, CancellationToken ct = default)
    {
        if (!await db.Sessions.AnyAsync(x => x.Id == sessionId, ct)) throw new KeyNotFoundException($"Session {sessionId} does not exist.");
        var temp = Path.Combine(Path.GetTempPath(), $"visualnotes-{Guid.NewGuid():N}.db");
        try
        {
            var source = (SqliteConnection)db.Database.GetDbConnection();
            if (source.State != System.Data.ConnectionState.Open) await source.OpenAsync(ct);
            await using (var target = new SqliteConnection($"Data Source={temp}")) { await target.OpenAsync(ct); source.BackupDatabase(target); }
            await using (var sanitized = new SqliteConnection($"Data Source={temp}"))
            {
                await sanitized.OpenAsync(ct);
                await using var command = sanitized.CreateCommand();
                command.CommandText = "DELETE FROM Settings WHERE IsSecret = 1; UPDATE ProviderProfiles SET ApiKeyReference = NULL;";
                await command.ExecuteNonQueryAsync(ct);
                command.CommandText = "VACUUM;";
                await command.ExecuteNonQueryAsync(ct);
            }

            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            await AddFileAsync(archive, temp, "database/visualnotes.db", ct);
            var manifest = archive.CreateEntry("manifest.json");
            await using (var stream = manifest.Open()) await JsonSerializer.SerializeAsync(stream, new { format = "visualnotes-session", version = FormatVersion, sessionId, createdAt = DateTimeOffset.UtcNow }, cancellationToken: ct);

            var sessionRoot = Path.GetFullPath(Path.Combine(dataDirectory, "sessions", sessionId.ToString("N")));
            if (Directory.Exists(sessionRoot))
                foreach (var file in Directory.EnumerateFiles(sessionRoot, "*", SearchOption.AllDirectories))
                    await AddFileAsync(archive, file, "files/" + Path.GetRelativePath(sessionRoot, file).Replace('\\', '/'), ct);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static async Task AddFileAsync(ZipArchive archive, string source, string name, CancellationToken ct)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var input = File.OpenRead(source);
        await using var output = entry.Open();
        await input.CopyToAsync(output, ct);
    }
}
