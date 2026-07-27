using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

public sealed record BoundingBoxNormalizationOptions(int MarginPixels = 0, int MinimumWidth = 2, int MinimumHeight = 2)
{
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MarginPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MinimumWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MinimumHeight);
    }
}

public sealed record NormalizedBoundingBox(PhysicalRectangle Pixels, IReadOnlyList<string> Warnings);

/// <summary>Converts model coordinates to the pixels of the exact source image and enforces crop safety.</summary>
public static class BoundingBoxNormalizer
{
    public static NormalizedBoundingBox Normalize(
        BoundingBox box, CoordinateSystem system, int imageWidth, int imageHeight,
        BoundingBoxNormalizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);
        options ??= new();
        options.Validate();

        if (!double.IsFinite(box.XMin) || !double.IsFinite(box.YMin) ||
            !double.IsFinite(box.XMax) || !double.IsFinite(box.YMax))
            throw new ArgumentException("Box coordinates must be finite.", nameof(box));
        if (box.XMin >= box.XMax || box.YMin >= box.YMax)
            throw new ArgumentException("Box must be non-empty and not inverted.", nameof(box));

        var scale = system switch
        {
            CoordinateSystem.Pixels => (X: 1d, Y: 1d),
            CoordinateSystem.Normalized01 => (X: (double)imageWidth, Y: (double)imageHeight),
            CoordinateSystem.Normalized1000 => (X: imageWidth / 1000d, Y: imageHeight / 1000d),
            _ => throw new ArgumentOutOfRangeException(nameof(system))
        };
        var x1 = box.XMin * scale.X - options.MarginPixels;
        var y1 = box.YMin * scale.Y - options.MarginPixels;
        var x2 = box.XMax * scale.X + options.MarginPixels;
        var y2 = box.YMax * scale.Y + options.MarginPixels;
        if (x2 <= 0 || y2 <= 0 || x1 >= imageWidth || y1 >= imageHeight)
            throw new ArgumentOutOfRangeException(nameof(box), "Box is entirely outside the image.");

        var clipped = x1 < 0 || y1 < 0 || x2 > imageWidth || y2 > imageHeight;
        var left = (int)Math.Floor(Math.Max(0, x1));
        var top = (int)Math.Floor(Math.Max(0, y1));
        var right = (int)Math.Ceiling(Math.Min(imageWidth, x2));
        var bottom = (int)Math.Ceiling(Math.Min(imageHeight, y2));
        var pixels = new PhysicalRectangle(left, top, right - left, bottom - top);
        if (pixels.Width < options.MinimumWidth || pixels.Height < options.MinimumHeight)
            throw new ArgumentException("Box is smaller than the configured minimum crop size.", nameof(box));

        return new(pixels, clipped ? ["La región se limitó a los bordes de la imagen."] : []);
    }

    public static IReadOnlyList<PhysicalRectangle> Consolidate(IEnumerable<PhysicalRectangle> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var result = new List<PhysicalRectangle>();
        foreach (var candidate in regions)
        {
            if (candidate.IsEmpty) throw new ArgumentException("Regions cannot be empty.", nameof(regions));
            var merged = candidate;
            var foundOverlap = true;
            while (foundOverlap)
            {
                foundOverlap = false;
                for (var index = result.Count - 1; index >= 0; index--)
                {
                    var current = result[index];
                    if (merged.X >= current.Right || current.X >= merged.Right ||
                        merged.Y >= current.Bottom || current.Y >= merged.Bottom) continue;
                    var left = Math.Min(merged.X, current.X);
                    var top = Math.Min(merged.Y, current.Y);
                    var right = Math.Max(merged.Right, current.Right);
                    var bottom = Math.Max(merged.Bottom, current.Bottom);
                    merged = new(left, top, right - left, bottom - top);
                    result.RemoveAt(index);
                    foundOverlap = true;
                }
            }
            result.Add(merged);
        }
        return result;
    }
}
