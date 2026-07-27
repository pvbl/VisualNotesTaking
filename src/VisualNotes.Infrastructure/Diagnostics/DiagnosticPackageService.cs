using System.IO.Compression;
using System.Text.Json;

namespace VisualNotes.Infrastructure.Diagnostics;

/// <summary>Creates an allow-list-only diagnostic archive without settings, databases, logs, or user files.</summary>
public sealed class DiagnosticPackageService
{
    public async Task CreateAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var entry = archive.CreateEntry("environment.json");
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, new
        {
            application = "VisualNotes",
            os = Environment.OSVersion.Platform.ToString(),
            runtime = Environment.Version.ToString()
        }, cancellationToken: cancellationToken);
    }
}
