using System.Text.Json;

using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

public sealed class CaptureDuplicateDetectionTests
{
    [Fact, Trait("Category", "Unit")]
    public void Calculates_stable_exact_and_perceptual_hashes()
    {
        var pixels = Enumerable.Range(0, 72).Select(value => (byte)value).ToArray();

        var result = CaptureHashCalculator.Calculate("image"u8, pixels, 9, 8);

        result.ExactHash.ShouldBe("6105d6cc76af400325e94d588ce511be5bfdbb73b437dc51eca43917d7a43e3d");
        CaptureHashCalculator.FormatPerceptualHash(result.PerceptualHash).ShouldBe("0000000000000000");
    }

    [Fact, Trait("Category", "Unit")]
    public void Perceptual_hash_records_luminance_decreases()
    {
        var pixels = Enumerable.Range(0, 8)
            .SelectMany(_ => Enumerable.Range(0, 9).Select(value => (byte)(8 - value)))
            .ToArray();

        var result = CaptureHashCalculator.Calculate("descending"u8, pixels, 9, 8);

        result.PerceptualHash.ShouldBe(ulong.MaxValue);
        CaptureHashCalculator.ParsePerceptualHash(
            CaptureHashCalculator.FormatPerceptualHash(result.PerceptualHash)).ShouldBe(ulong.MaxValue);
    }

    [Theory, Trait("Category", "Unit")]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(2, 2, 3)]
    public void Hashing_rejects_invalid_dimensions_or_pixel_count(int width, int height, int pixelCount)
    {
        var luminance = new byte[pixelCount];
        Should.Throw<ArgumentException>(() =>
            CaptureHashCalculator.Calculate("image"u8, luminance, width, height));
    }

    [Fact, Trait("Category", "Unit")]
    public void Only_compares_nearby_captures_in_the_same_section()
    {
        var section = Guid.NewGuid();
        var first = Capture(section, 0, "same", "0000000000000000");
        var close = Capture(section, 30, "same", "0000000000000000");
        var otherSection = Capture(Guid.NewGuid(), 40, "same", "0000000000000000");
        var late = Capture(section, 181, "same", "0000000000000000");

        var results = new CaptureDuplicateDetector().Compare([late, otherSection, close, first]);

        results.ShouldHaveSingleItem().Later.ShouldBe(close);
    }

    [Fact, Trait("Category", "Unit")]
    public void Conservative_rules_distinguish_exact_near_progressive_and_distinct()
    {
        var detector = new CaptureDuplicateDetector();
        var original = Capture(Guid.Empty, 0, "same", "0000000000000000");

        detector.ComparePair(original, Capture(Guid.Empty, 1, "same", "ffffffffffffffff")).Similarity.ShouldBe(CaptureSimilarity.ExactDuplicate);
        var near = detector.ComparePair(original, Capture(Guid.Empty, 1, "changed", "0000000000000007"));
        near.Similarity.ShouldBe(CaptureSimilarity.NearDuplicate);
        near.SuggestedResolution.ShouldBe(DuplicateResolution.Keep);
        detector.ComparePair(original, Capture(Guid.Empty, 1, "changed", "000000000000003f")).Similarity.ShouldBe(CaptureSimilarity.ProgressiveChange);
        detector.ComparePair(original, Capture(Guid.Empty, 1, "changed", "000000000000ffff")).Similarity.ShouldBe(CaptureSimilarity.Distinct);
    }

    [Fact, Trait("Category", "Unit")]
    public void Missing_hashes_and_dimension_mismatches_are_never_auto_deduplicated()
    {
        var detector = new CaptureDuplicateDetector();
        var missing = Capture(Guid.Empty, 0, string.Empty, string.Empty);
        var otherMissing = Capture(Guid.Empty, 1, string.Empty, string.Empty);
        otherMissing.Width = 0;

        detector.ComparePair(missing, otherMissing).Similarity.ShouldBe(CaptureSimilarity.Distinct);
        CaptureDuplicateDetector.HammingDistance(null, "0").ShouldBe(64);
        CaptureDuplicateDetector.HammingDistance("0", null).ShouldBe(64);
    }

    [Fact, Trait("Category", "Unit")]
    public void Exact_hash_matching_is_case_insensitive_but_requires_a_non_blank_hash()
    {
        var detector = new CaptureDuplicateDetector();
        detector.ComparePair(
            Capture(Guid.Empty, 0, "ABCDEF", "0"),
            Capture(Guid.Empty, 1, "abcdef", "0")).Similarity.ShouldBe(CaptureSimilarity.ExactDuplicate);

        detector.ComparePair(
            Capture(Guid.Empty, 0, " ", "0"),
            Capture(Guid.Empty, 1, " ", "0")).Similarity.ShouldBe(CaptureSimilarity.NearDuplicate);
    }

    [Fact, Trait("Category", "Unit")]
    public void Empty_quality_dataset_reports_zero_for_undefined_rates()
    {
        var metrics = new DuplicateEvaluation(0, 0, 0);

        metrics.Precision.ShouldBe(0);
        metrics.Recall.ShouldBe(0);
        metrics.F1.ShouldBe(0);
        metrics.FalsePositiveRate(0).ShouldBe(0);
    }

    [Fact, Trait("Category", "Unit")]
    public void Labeled_dataset_has_zero_false_positives_and_reports_quality_metrics()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "duplicate-detection", "v1", "dataset.json"));
        var dataset = JsonSerializer.Deserialize<Dataset>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var detector = new CaptureDuplicateDetector();
        var predictions = dataset.Cases.Select(item => (item.Duplicate,
            Predicted: detector.ComparePair(Capture(Guid.Empty, 0, item.ExactA, item.HashA), Capture(Guid.Empty, 1, item.ExactB, item.HashB)).Similarity
                is CaptureSimilarity.ExactDuplicate or CaptureSimilarity.NearDuplicate)).ToArray();
        var metrics = new DuplicateEvaluation(
            predictions.Count(x => x.Duplicate && x.Predicted),
            predictions.Count(x => !x.Duplicate && x.Predicted),
            predictions.Count(x => x.Duplicate && !x.Predicted));
        var trueNegatives = predictions.Count(x => !x.Duplicate && !x.Predicted);

        metrics.Precision.ShouldBe(1d);
        metrics.Recall.ShouldBe(1d);
        metrics.F1.ShouldBe(1d);
        metrics.FalsePositiveRate(trueNegatives).ShouldBe(0d);
    }

    [Fact, Trait("Category", "Unit")]
    public void User_can_keep_exclude_or_combine_and_undo_combination()
    {
        var retained = Capture(Guid.Empty, 0, "one", "0");
        retained.Tags = "diagram";
        retained.UserContext = "first";
        var duplicate = Capture(Guid.Empty, 1, "two", "0");
        duplicate.Tags = "exam, diagram";
        duplicate.UserContext = "second";
        var library = new CaptureLibrary([retained, duplicate]);

        library.ResolveDuplicate(retained.Id, [duplicate.Id], DuplicateResolution.Keep);
        duplicate.IncludeInDocument.ShouldBeTrue();
        library.ResolveDuplicate(retained.Id, [duplicate.Id], DuplicateResolution.Combine);
        duplicate.IncludeInDocument.ShouldBeFalse();
        retained.Tags.ShouldBe("diagram, exam");
        retained.UserContext.ShouldBe($"first{Environment.NewLine}second");
        library.Undo().ShouldBeTrue();
        duplicate.IncludeInDocument.ShouldBeTrue();
        retained.Tags.ShouldBe("diagram");

        library.ResolveDuplicate(retained.Id, [duplicate.Id], DuplicateResolution.Exclude);
        duplicate.ProcessingStatus.ShouldBe(ScreenshotStatus.Excluded);
    }

    private static Screenshot Capture(Guid? section, int seconds, string exact, string perceptual) => new()
    {
        SectionId = section,
        CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(seconds),
        Width = 1920,
        Height = 1080,
        Image = new ScreenshotImage { Sha256 = exact },
        PerceptualHash = perceptual
    };

    private sealed record Dataset(int Version, string Description, DatasetCase[] Cases);
    private sealed record DatasetCase(string Id, string ExactA, string ExactB, string HashA, string HashB, bool Duplicate);
}
