using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using VisualNotes.Core.Services;

namespace VisualNotes.Infrastructure.Persistence;

public sealed class ScreenshotStorageService(
    string dataDirectory,
    long reservedFreeBytes = 16 * 1024 * 1024,
    long maximumInputBytes = 32 * 1024 * 1024,
    long maximumPixels = 100_000_000) : IScreenshotStorageService
{
    public async Task<StoredScreenshot> StoreAsync(ScreenshotStorageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request.Content);
        var max = request.SizePreset switch { ScreenshotSizePreset.Default2560 => 2560, ScreenshotSizePreset.FullHd1920 => 1920, ScreenshotSizePreset.Original => int.MaxValue, ScreenshotSizePreset.Custom when request.CustomMaximumSide > 0 => request.CustomMaximumSide.Value, _ => throw new ArgumentOutOfRangeException(nameof(request.CustomMaximumSide)) };
        if (request.ThumbnailMaximumSide <= 0) throw new ArgumentOutOfRangeException(nameof(request.ThumbnailMaximumSide));
        var paths = ScreenshotPaths.For(request.SessionId);
        CreateWorkspace(paths);

        if (maximumInputBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumInputBytes));
        if (maximumPixels < 1) throw new ArgumentOutOfRangeException(nameof(maximumPixels));
        using var input = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await request.Content.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (input.Length + read > maximumInputBytes)
                throw new InvalidDataException($"Screenshot exceeds the {maximumInputBytes}-byte input limit.");
            await input.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        EnsureSpace(input.Length * 3 + reservedFreeBytes);
        input.Position = 0;
        using var image = await Image.LoadAsync(input, cancellationToken); // ImageSharp reports corrupt/truncated input.
        if ((long)image.Width * image.Height > maximumPixels)
            throw new InvalidDataException($"Screenshot exceeds the {maximumPixels}-pixel decoded image limit.");
        var originalWidth = image.Width; var originalHeight = image.Height;
        var usePng = request.ContentKind == ScreenshotContentKind.CodeOrSmallText;
        var extension = usePng ? ".png" : ".jpg";

        input.Position = 0;
        var originalExtension = image.Metadata.DecodedImageFormat?.FileExtensions.FirstOrDefault() is { } ext ? "." + ext : ".img";
        var original = await WriteAsync(paths.Originals, request.ScreenshotId, originalExtension, input.ToArray(), originalWidth, originalHeight, originalWidth, originalHeight, image.Metadata.DecodedImageFormat?.Name ?? "unknown", null, cancellationToken);
        using var optimizedImage = image.Clone(x => ResizeToFit(x, max, image.Width, image.Height));
        var optimizedBytes = await EncodeAsync(optimizedImage, usePng, cancellationToken);
        var optimized = await WriteAsync(paths.Optimized, request.ScreenshotId, extension, optimizedBytes, originalWidth, originalHeight, optimizedImage.Width, optimizedImage.Height, usePng ? "PNG" : "JPEG", usePng ? null : 88, cancellationToken);
        using var thumbnailImage = image.Clone(x => ResizeToFit(x, request.ThumbnailMaximumSide, image.Width, image.Height));
        var thumbnailBytes = await EncodeAsync(thumbnailImage, usePng, cancellationToken);
        var thumbnail = await WriteAsync(paths.Thumbnails, request.ScreenshotId, extension, thumbnailBytes, originalWidth, originalHeight, thumbnailImage.Width, thumbnailImage.Height, usePng ? "PNG" : "JPEG", usePng ? null : 88, cancellationToken);
        return new(original, optimized, thumbnail);
    }

    private void CreateWorkspace(ScreenshotPaths p)
    {
        foreach (var relative in new[] { p.Originals, p.Optimized, p.Thumbnails, p.Crops, p.Analysis, p.Exports }) Directory.CreateDirectory(StoragePath.Resolve(dataDirectory, relative));
    }

    private void EnsureSpace(long needed)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(dataDirectory))!;
        if (new DriveInfo(root).AvailableFreeSpace < needed) throw new IOException("Insufficient disk space to store screenshot.");
    }

    private static void ResizeToFit(IImageProcessingContext context, int maximumSide, int width, int height)
    {
        if (Math.Max(width, height) > maximumSide)
            context.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new(maximumSide, maximumSide), Sampler = KnownResamplers.Lanczos3 });
    }

    private static async Task<byte[]> EncodeAsync(Image image, bool png, CancellationToken ct)
    {
        using var output = new MemoryStream();
        if (png) await image.SaveAsync(output, new PngEncoder { CompressionLevel = PngCompressionLevel.DefaultCompression }, ct);
        else await image.SaveAsync(output, new JpegEncoder { Quality = 88 }, ct);
        return output.ToArray();
    }

    private async Task<StoredImageMetadata> WriteAsync(string folder, Guid id, string extension, byte[] bytes, int ow, int oh, int fw, int fh, string format, int? quality, CancellationToken ct)
    {
        var relative = folder + "/" + id.ToString("N") + extension.ToLowerInvariant();
        var target = StoragePath.Resolve(dataDirectory, relative);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            EnsureSpace(bytes.LongLength + reservedFreeBytes);
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct); }
            File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return new(relative, ow, oh, fw, fh, Math.Min((double)fw / ow, (double)fh / oh), format, quality, Convert.ToHexString(SHA256.HashData(bytes)), bytes.LongLength);
    }
}
