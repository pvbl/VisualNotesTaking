using System.Security.Cryptography;
using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

public enum CaptureSimilarity { ExactDuplicate, NearDuplicate, ProgressiveChange, Distinct }
public enum DuplicateResolution { Keep, Exclude, Combine }

public sealed record CaptureFingerprint(string ExactHash, ulong PerceptualHash, int Width, int Height);

public sealed record CaptureComparison(
    Screenshot Earlier,
    Screenshot Later,
    CaptureSimilarity Similarity,
    int PerceptualDistance,
    DuplicateResolution SuggestedResolution);

public sealed record DuplicateDetectionOptions(
    TimeSpan MaximumCaptureDistance,
    int NearDuplicateDistance = 3,
    int ProgressiveChangeDistance = 8)
{
    public static DuplicateDetectionOptions Conservative { get; } = new(TimeSpan.FromMinutes(2));
}

/// <summary>Computes a byte-exact SHA-256 and a translation-tolerant 64-bit difference hash.</summary>
public static class CaptureHashCalculator
{
    public static CaptureFingerprint Calculate(ReadOnlySpan<byte> encodedImage, ReadOnlySpan<byte> luminance, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (luminance.Length != checked(width * height))
            throw new ArgumentException("Luminance must contain exactly one byte per pixel.", nameof(luminance));

        var exact = Convert.ToHexString(SHA256.HashData(encodedImage)).ToLowerInvariant();
        ulong perceptual = 0;
        for (var y = 0; y < 8; y++)
        {
            var sourceY = Math.Min(height - 1, y * height / 8);
            for (var x = 0; x < 8; x++)
            {
                var leftX = Math.Min(width - 1, x * width / 9);
                var rightX = Math.Min(width - 1, (x + 1) * width / 9);
                if (luminance[(sourceY * width) + leftX] > luminance[(sourceY * width) + rightX])
                    perceptual |= 1UL << ((y * 8) + x);
            }
        }

        return new(exact, perceptual, width, height);
    }

    public static string FormatPerceptualHash(ulong hash) => hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    public static ulong ParsePerceptualHash(string hash) => ulong.Parse(hash, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Compares only temporally close captures in the same section. Conservative defaults only
/// auto-exclude byte-identical images; perceptual matches always remain visible for review.
/// </summary>
public sealed class CaptureDuplicateDetector
{
    private readonly DuplicateDetectionOptions _options;

    public CaptureDuplicateDetector(DuplicateDetectionOptions? options = null) =>
        _options = options ?? DuplicateDetectionOptions.Conservative;

    public IReadOnlyList<CaptureComparison> Compare(IEnumerable<Screenshot> captures)
    {
        var ordered = captures.OrderBy(capture => capture.CapturedAt).ToArray();
        var results = new List<CaptureComparison>();
        for (var laterIndex = 1; laterIndex < ordered.Length; laterIndex++)
        {
            var later = ordered[laterIndex];
            for (var earlierIndex = laterIndex - 1; earlierIndex >= 0; earlierIndex--)
            {
                var earlier = ordered[earlierIndex];
                if (later.CapturedAt - earlier.CapturedAt > _options.MaximumCaptureDistance) break;
                if (earlier.SectionId != later.SectionId) continue;
                results.Add(ComparePair(earlier, later));
            }
        }
        return results;
    }

    public CaptureComparison ComparePair(Screenshot earlier, Screenshot later)
    {
        var exact = earlier.Image?.Sha256;
        var distance = HammingDistance(earlier.PerceptualHash, later.PerceptualHash);
        CaptureSimilarity similarity;
        DuplicateResolution resolution;

        if (!string.IsNullOrWhiteSpace(exact) && string.Equals(exact, later.Image?.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            similarity = CaptureSimilarity.ExactDuplicate;
            resolution = DuplicateResolution.Exclude;
        }
        else if (SameDimensions(earlier, later) && distance <= _options.NearDuplicateDistance)
        {
            similarity = CaptureSimilarity.NearDuplicate;
            resolution = DuplicateResolution.Keep;
        }
        else if (SameDimensions(earlier, later) && distance <= _options.ProgressiveChangeDistance)
        {
            similarity = CaptureSimilarity.ProgressiveChange;
            resolution = DuplicateResolution.Keep;
        }
        else
        {
            similarity = CaptureSimilarity.Distinct;
            resolution = DuplicateResolution.Keep;
        }

        return new(earlier, later, similarity, distance, resolution);
    }

    public static int HammingDistance(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return 64;
        var difference = CaptureHashCalculator.ParsePerceptualHash(left) ^ CaptureHashCalculator.ParsePerceptualHash(right);
        return System.Numerics.BitOperations.PopCount(difference);
    }

    private static bool SameDimensions(Screenshot left, Screenshot right) =>
        left.Width > 0 && left.Width == right.Width && left.Height > 0 && left.Height == right.Height;
}

public sealed record DuplicateEvaluation(int TruePositives, int FalsePositives, int FalseNegatives)
{
    public double Precision => Ratio(TruePositives, TruePositives + FalsePositives);
    public double Recall => Ratio(TruePositives, TruePositives + FalseNegatives);
    public double F1 => Precision + Recall <= double.Epsilon ? 0 : 2 * Precision * Recall / (Precision + Recall);
    public double FalsePositiveRate(int trueNegatives) => Ratio(FalsePositives, FalsePositives + trueNegatives);
    private static double Ratio(int numerator, int denominator) => denominator == 0 ? 0 : (double)numerator / denominator;
}
