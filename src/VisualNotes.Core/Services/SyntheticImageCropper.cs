using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

/// <summary>Crops tightly packed RGBA pixels. Also provides a deterministic seam for capture integration tests.</summary>
public static class SyntheticImageCropper
{
    public static byte[] Crop(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight, PhysicalRectangle region)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentNullException.ThrowIfNull(region);
        if (region.X < 0 || region.Y < 0 || region.Right > sourceWidth || region.Bottom > sourceHeight || region.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(region));
        if (source.Length != checked(sourceWidth * sourceHeight * 4)) throw new ArgumentException("Expected tightly packed RGBA pixels.", nameof(source));
        var result = new byte[checked(region.Width * region.Height * 4)];
        for (var row = 0; row < region.Height; row++)
            source.Slice(((region.Y + row) * sourceWidth + region.X) * 4, region.Width * 4).CopyTo(result.AsSpan(row * region.Width * 4));
        return result;
    }
}
