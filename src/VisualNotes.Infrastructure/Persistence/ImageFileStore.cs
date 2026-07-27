using System.Security.Cryptography;

namespace VisualNotes.Infrastructure.Persistence;

public sealed class ImageFileStore(string dataDirectory)
{
    public async Task<(string RelativePath, long Length, string Sha256)> SaveAsync(Guid sessionId, Guid screenshotId, Stream content, string extension = ".png", CancellationToken ct = default)
    {
        var relative = Path.Combine("sessions", sessionId.ToString("N"), "captures", screenshotId.ToString("N") + extension);
        var absolute = Path.GetFullPath(Path.Combine(dataDirectory, relative));
        var root = Path.GetFullPath(dataDirectory) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(root, StringComparison.Ordinal)) throw new InvalidOperationException("Image path escapes the application data directory.");
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        await using var file = File.Create(absolute);
        using var hash = SHA256.Create();
        await using var crypto = new CryptoStream(file, hash, CryptoStreamMode.Write);
        await content.CopyToAsync(crypto, ct);
        await crypto.FlushFinalBlockAsync(ct);
        return (relative.Replace(Path.DirectorySeparatorChar, '/'), file.Length, Convert.ToHexString(hash.Hash!));
    }
}
