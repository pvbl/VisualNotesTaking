using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace VisualNotes.Infrastructure.Persistence;

public sealed record BackupEntry(string Path, long Length, string Sha256);
public sealed record BackupManifest(string Format, int Version, Guid SessionId, DateTimeOffset CreatedAt,
    IReadOnlyList<BackupEntry>? Entries = null);
public sealed record BackupValidationResult(BackupManifest Manifest, IReadOnlyList<BackupEntry> Entries);

/// <summary>
/// Creates credential-free, self-validating backups.  A backup written to a path is published with a
/// single rename, so a crash can leave only an ignorable .tmp file and never a half-valid backup.
/// </summary>
public sealed class SessionBackupService(VisualNotesDbContext db, string dataDirectory)
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { PropertyNameCaseInsensitive = true };
    public const int FormatVersion = 2;
    public const int OldestSupportedFormatVersion = 1;

    public async Task CreateAsync(Guid sessionId, Stream destination, CancellationToken ct = default)
    {
        if (!destination.CanWrite) throw new ArgumentException("The backup destination is not writable.", nameof(destination));
        if (!await db.Sessions.AnyAsync(x => x.Id == sessionId, ct))
            throw new KeyNotFoundException($"Session {sessionId} does not exist.");

        var snapshot = Path.Combine(Path.GetTempPath(), $"visualnotes-{Guid.NewGuid():N}.db");
        try
        {
            await CreateSanitizedSnapshotAsync(snapshot, ct);
            var sources = new List<(string Source, string ArchivePath)> { (snapshot, "database/visualnotes.db") };
            var sessionRoot = Path.GetFullPath(Path.Combine(dataDirectory, "sessions", sessionId.ToString("N")));
            if (Directory.Exists(sessionRoot))
                sources.AddRange(Directory.EnumerateFiles(sessionRoot, "*", SearchOption.AllDirectories)
                    .Select(file => (file, "files/" + Path.GetRelativePath(sessionRoot, file).Replace('\\', '/'))));

            var entries = new List<BackupEntry>();
            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var source in sources.OrderBy(x => x.ArchivePath, StringComparer.Ordinal))
                entries.Add(await AddFileAsync(archive, source.Source, source.ArchivePath, ct));
            var manifest = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using var stream = manifest.Open();
            await JsonSerializer.SerializeAsync(stream,
                new BackupManifest("visualnotes-session", FormatVersion, sessionId, DateTimeOffset.UtcNow, entries),
                cancellationToken: ct);
        }
        finally { TryDelete(snapshot); }
    }

    /// <summary>Creates either a user-requested or scheduled backup atomically. No credentials are included.</summary>
    public async Task<string> CreateFileAsync(Guid sessionId, string destinationPath, CancellationToken ct = default)
    {
        var destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await CreateAsync(sessionId, output, ct);
                await output.FlushAsync(ct);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally { TryDelete(temporary); }
    }

    public Task<string> CreateAutomaticAsync(Guid sessionId, string backupDirectory, CancellationToken ct = default)
    {
        var name = $"visualnotes-{sessionId:N}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.vnotes";
        return CreateFileAsync(sessionId, Path.Combine(backupDirectory, name), ct);
    }

    public static async Task<BackupValidationResult> ValidateAsync(Stream source, CancellationToken ct = default)
    {
        if (!source.CanRead || !source.CanSeek) throw new ArgumentException("The backup stream must be readable and seekable.", nameof(source));
        source.Position = 0;
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        EnsureSafeAndUniqueNames(archive);
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Backup manifest is missing.");
        BackupManifest manifest;
        await using (var input = manifestEntry.Open())
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(input, ManifestJsonOptions, ct)
                ?? throw new InvalidDataException("Backup manifest is invalid.");
        if (manifest.Format != "visualnotes-session" || manifest.Version is < OldestSupportedFormatVersion or > FormatVersion)
            throw new InvalidDataException($"Unsupported backup format version {manifest.Version}.");
        var database = archive.GetEntry("database/visualnotes.db") ?? throw new InvalidDataException("Backup database is missing.");

        var actual = new List<BackupEntry>();
        foreach (var entry in archive.Entries.Where(x => x.FullName != "manifest.json").OrderBy(x => x.FullName, StringComparer.Ordinal))
            actual.Add(await HashAsync(entry, ct));
        if (manifest.Version >= 2)
        {
            try
            {
                var expected = (manifest.Entries ?? throw new InvalidDataException("Backup checksums are missing."))
                    .ToDictionary(x => x.Path, StringComparer.Ordinal);
                if (expected.Count != actual.Count || actual.Any(x => !expected.TryGetValue(x.Path, out var item) || item.Length != x.Length || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(item.Sha256), Convert.FromHexString(x.Sha256))))
                    throw new InvalidDataException("Backup integrity validation failed.");
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            { throw new InvalidDataException("Backup checksum metadata is invalid.", exception); }
        }

        var temporary = Path.Combine(Path.GetTempPath(), $"visualnotes-validate-{Guid.NewGuid():N}.db");
        try
        {
            await using (var output = File.Create(temporary))
            await using (var input = database.Open()) await input.CopyToAsync(output, ct);
            await using var connection = new SqliteConnection($"Data Source={temporary};Mode=ReadOnly");
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            if (!string.Equals((string?)await command.ExecuteScalarAsync(ct), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The backup database failed SQLite integrity validation.");
        }
        catch (SqliteException exception) { throw new InvalidDataException("The backup database is invalid.", exception); }
        finally { TryDelete(temporary); }
        return new(manifest, actual);
    }

    /// <summary>
    /// Restores a validated backup into an offline data directory. Every extracted file is first written
    /// beside its destination and fsynced; publishing is atomic and the operation is safe to repeat after a crash.
    /// </summary>
    public static async Task<BackupValidationResult> RestoreAsync(string backupPath, string targetDataDirectory, CancellationToken ct = default)
    {
        BackupValidationResult validation;
        await using (var validationStream = File.OpenRead(backupPath))
            validation = await ValidateAsync(validationStream, ct);

        await using var source = File.OpenRead(backupPath);
        using var archive = new ZipArchive(source, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("manifest.json")!;
        BackupManifest manifest;
        await using (var manifestStream = manifestEntry.Open())
            manifest = (await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, ManifestJsonOptions, ct))!;

        Directory.CreateDirectory(targetDataDirectory);
        var publications = new List<(string Temporary, string Destination)>();
        try
        {
            foreach (var entry in archive.Entries.Where(x => x.FullName != "manifest.json" && !x.FullName.EndsWith('/')))
            {
                var relative = entry.FullName == "database/visualnotes.db"
                    ? "visualnotes.db"
                    : entry.FullName.StartsWith("files/", StringComparison.Ordinal)
                        ? Path.Combine("sessions", manifest.SessionId.ToString("N"), entry.FullName[6..].Replace('/', Path.DirectorySeparatorChar))
                        : throw new InvalidDataException($"Unexpected backup entry: {entry.FullName}");
                var destination = StoragePath.Resolve(targetDataDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temporary = destination + ".restore-" + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
                await using (var input = entry.Open())
                {
                    await input.CopyToAsync(output, ct);
                    await output.FlushAsync(ct);
                    output.Flush(flushToDisk: true);
                }
                publications.Add((temporary, destination));
            }
            // Publish the database last: files appearing early are harmless, while a visible DB must never
            // reference files which have not yet reached their final names.
            foreach (var item in publications.OrderBy(x => x.Destination.EndsWith("visualnotes.db", StringComparison.Ordinal) ? 1 : 0))
                File.Move(item.Temporary, item.Destination, overwrite: true);
            return new(manifest, validation.Entries);
        }
        finally { foreach (var item in publications) TryDelete(item.Temporary); }
    }

    private async Task CreateSanitizedSnapshotAsync(string path, CancellationToken ct)
    {
        var source = (SqliteConnection)db.Database.GetDbConnection();
        if (source.State != System.Data.ConnectionState.Open) await source.OpenAsync(ct);
        var snapshotConnectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        await using (var target = new SqliteConnection(snapshotConnectionString))
        { await target.OpenAsync(ct); source.BackupDatabase(target); }
        await using var sanitized = new SqliteConnection(snapshotConnectionString);
        await sanitized.OpenAsync(ct);
        await using var transaction = await sanitized.BeginTransactionAsync(ct);
        await using var command = sanitized.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "DELETE FROM Settings WHERE IsSecret = 1; UPDATE ProviderProfiles SET ApiKeyReference = NULL;";
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        command.Transaction = null;
        command.CommandText = "VACUUM;";
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void EnsureSafeAndUniqueNames(ZipArchive archive)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            if (normalized.StartsWith('/') || normalized.Split('/').Any(x => x == "..") || !names.Add(normalized))
                throw new InvalidDataException($"Unsafe or duplicate backup entry: {entry.FullName}");
        }
    }

    private static async Task<BackupEntry> AddFileAsync(ZipArchive archive, string source, string name, CancellationToken ct)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var input = File.OpenRead(source);
        await using var output = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long length = 0; int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0)
        { await output.WriteAsync(buffer.AsMemory(0, read), ct); hash.AppendData(buffer, 0, read); length += read; }
        return new(name, length, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static async Task<BackupEntry> HashAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        await using var input = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long length = 0; int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0) { hash.AppendData(buffer, 0, read); length += read; }
        return new(entry.FullName, length, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
}
