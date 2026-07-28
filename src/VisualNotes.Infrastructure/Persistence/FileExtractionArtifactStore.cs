using System.Text.Json;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.Infrastructure.Persistence;

/// <summary>Durable, provider-independent storage for normalized extractions and deterministic RGBA crops.</summary>
public sealed class FileExtractionArtifactStore
    : IExtractionArtifactStore
{
    private readonly string _root;

    public FileExtractionArtifactStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _root = Path.Combine(Path.GetFullPath(dataDirectory), "extractions");
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveExtractionAsync(string normalizedJson, CancellationToken cancellationToken = default)
    {
        _ = StructuredAnalysisResponseParser.Parse(normalizedJson, new(false));
        var id = Guid.NewGuid().ToString("N");
        var directory = DirectoryFor(id);
        Directory.CreateDirectory(Path.Combine(directory, "crops"));
        await File.WriteAllTextAsync(Path.Combine(directory, "extraction.json"), normalizedJson, cancellationToken).ConfigureAwait(false);
        await WriteCropIndexAsync(directory, [], cancellationToken).ConfigureAwait(false);
        return id;
    }

    public async Task<string> SaveCropAsync(string extractionId, string label, PhysicalRectangle pixels, ReadOnlyMemory<byte> rgbaPixels, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.IsEmpty || rgbaPixels.Length != checked(pixels.Width * pixels.Height * 4)) throw new ArgumentException("Crop must be tightly packed RGBA.", nameof(rgbaPixels));
        var directory = ExistingDirectoryFor(extractionId);
        var fileName = $"{Guid.NewGuid():N}.rgba";
        var relativePath = Path.Combine("crops", fileName).Replace('\\', '/');
        await File.WriteAllBytesAsync(Path.Combine(directory, "crops", fileName), rgbaPixels.ToArray(), cancellationToken).ConfigureAwait(false);
        var crops = await ReadCropIndexAsync(directory, cancellationToken).ConfigureAwait(false);
        crops.Add(new(label, pixels, relativePath));
        await WriteCropIndexAsync(directory, crops, cancellationToken).ConfigureAwait(false);
        return relativePath;
    }

    public async Task<PersistedExtraction> LoadExtractionAsync(string extractionId, CancellationToken cancellationToken = default)
    {
        var directory = ExistingDirectoryFor(extractionId);
        var json = await File.ReadAllTextAsync(Path.Combine(directory, "extraction.json"), cancellationToken).ConfigureAwait(false);
        _ = StructuredAnalysisResponseParser.Parse(json, new(false));
        return new(extractionId, json, await ReadCropIndexAsync(directory, cancellationToken).ConfigureAwait(false));
    }

    private string DirectoryFor(string id)
    {
        if (id.Length != 32 || id.Any(character => !char.IsAsciiHexDigit(character))) throw new ArgumentException("Invalid extraction identifier.", nameof(id));
        return Path.Combine(_root, id);
    }

    private string ExistingDirectoryFor(string id)
    {
        var directory = DirectoryFor(id);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Extraction '{id}' does not exist.");
        return directory;
    }

    private static async Task<List<PersistedCrop>> ReadCropIndexAsync(string directory, CancellationToken cancellationToken) =>
        JsonSerializer.Deserialize<List<PersistedCrop>>(await File.ReadAllTextAsync(Path.Combine(directory, "crops.json"), cancellationToken).ConfigureAwait(false)) ?? [];

    private static Task WriteCropIndexAsync(string directory, IReadOnlyList<PersistedCrop> crops, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(Path.Combine(directory, "crops.json"), JsonSerializer.Serialize(crops), cancellationToken);
}
