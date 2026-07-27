using System.Security.Cryptography;

namespace VisualNotes.Infrastructure.Persistence;

/// <summary>Low-level atomic file writer retained for non-image callers.</summary>
public sealed class ImageFileStore(string dataDirectory)
{
    public async Task<(string RelativePath, long Length, string Sha256)> SaveAsync(Guid sessionId, Guid screenshotId, Stream content, string extension = ".png", CancellationToken ct = default)
    {
        var relative = ScreenshotPaths.For(sessionId).Originals + "/" + screenshotId.ToString("N") + extension;
        var absolute = StoragePath.Resolve(dataDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        var temp = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await content.CopyToAsync(file, ct); await file.FlushAsync(ct); }
            File.Move(temp, absolute, true);
            await using var read = File.OpenRead(absolute);
            return (relative, read.Length, Convert.ToHexString(await SHA256.HashDataAsync(read, ct)));
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed record ScreenshotPaths(string Originals, string Optimized, string Thumbnails, string Crops, string Analysis, string Exports)
{
    public static ScreenshotPaths For(Guid sessionId)
    {
        var root = "sessions/" + sessionId.ToString("N");
        return new($"{root}/originals", $"{root}/optimized", $"{root}/thumbnails", $"{root}/crops", $"{root}/analysis", $"{root}/exports");
    }
}

internal static class StoragePath
{
    public static string Resolve(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.Ordinal)) throw new InvalidOperationException("Storage path escapes the data directory.");
        return full;
    }
}
