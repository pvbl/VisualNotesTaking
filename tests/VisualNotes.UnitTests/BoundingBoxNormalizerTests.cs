using FsCheck;
using FsCheck.Xunit;

using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

[Trait("Category", "Unit")]
public sealed class BoundingBoxNormalizerTests
{
    [Theory]
    [InlineData(CoordinateSystem.Pixels, 10, 20, 30, 40, 10, 20, 20, 20)]
    [InlineData(CoordinateSystem.Normalized01, .1, .2, .3, .4, 10, 20, 20, 20)]
    [InlineData(CoordinateSystem.Normalized1000, 100, 200, 300, 400, 10, 20, 20, 20)]
    public void Converts_every_scale_against_exact_image(
        CoordinateSystem system, double x1, double y1, double x2, double y2,
        int expectedX, int expectedY, int expectedWidth, int expectedHeight)
    {
        var result = BoundingBoxNormalizer.Normalize(new(y1, x1, y2, x2), system, 100, 100);
        result.Pixels.ShouldBe(new PhysicalRectangle(expectedX, expectedY, expectedWidth, expectedHeight));
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Rounds_outward_so_fractional_content_is_not_lost()
    {
        var result = BoundingBoxNormalizer.Normalize(new(2.9, 1.1, 8.01, 9.001), CoordinateSystem.Pixels, 20, 20);
        result.Pixels.ShouldBe(new PhysicalRectangle(1, 2, 9, 7));
    }

    [Fact]
    public void Applies_margin_then_clips_and_warns()
    {
        var result = BoundingBoxNormalizer.Normalize(new(1, 1, 9, 9), CoordinateSystem.Pixels, 10, 10, new(3));
        result.Pixels.ShouldBe(new PhysicalRectangle(0, 0, 10, 10));
        result.Warnings.Single().ShouldContain("bordes");
    }

    [Theory]
    [InlineData(1, 1, 1, 3)]
    [InlineData(4, 4, 2, 5)]
    public void Rejects_empty_or_inverted(double y1, double x1, double y2, double x2) =>
        Should.Throw<ArgumentException>(() => BoundingBoxNormalizer.Normalize(new(y1, x1, y2, x2), CoordinateSystem.Pixels, 10, 10));

    [Fact]
    public void Rejects_fully_outside_and_too_small_regions()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => BoundingBoxNormalizer.Normalize(new(20, 20, 30, 30), CoordinateSystem.Pixels, 10, 10));
        Should.Throw<ArgumentException>(() => BoundingBoxNormalizer.Normalize(new(1, 1, 1.5, 1.5), CoordinateSystem.Pixels, 10, 10));
    }

    [Theory]
    [InlineData(double.NaN, 0, 1, 1)]
    [InlineData(0, double.PositiveInfinity, 1, 1)]
    [InlineData(0, 0, double.NegativeInfinity, 1)]
    [InlineData(0, 0, 1, double.NaN)]
    public void Rejects_each_non_finite_coordinate(double y1, double x1, double y2, double x2) =>
        Should.Throw<ArgumentException>(() =>
            BoundingBoxNormalizer.Normalize(new(y1, x1, y2, x2), CoordinateSystem.Pixels, 10, 10));

    [Fact]
    public void Rejects_unknown_coordinate_system() =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            BoundingBoxNormalizer.Normalize(new(0, 0, 1, 1), (CoordinateSystem)999, 10, 10));

    [Theory]
    [InlineData(0, 10, 5, 15)]
    [InlineData(10, 0, 15, 5)]
    [InlineData(-5, 0, 0, 5)]
    [InlineData(0, -5, 5, 0)]
    public void Rejects_regions_outside_each_image_edge(double y1, double x1, double y2, double x2) =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            BoundingBoxNormalizer.Normalize(new(y1, x1, y2, x2), CoordinateSystem.Pixels, 10, 10));

    [Theory]
    [InlineData(-1, 2, 2)]
    [InlineData(0, 0, 2)]
    [InlineData(0, 2, 0)]
    public void Rejects_invalid_normalization_options(int margin, int minimumWidth, int minimumHeight) =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            BoundingBoxNormalizer.Normalize(new(0, 0, 5, 5), CoordinateSystem.Pixels, 10, 10,
                new(margin, minimumWidth, minimumHeight)));

    [Fact]
    public void Consolidates_transitively_overlapping_regions_but_not_touching_regions()
    {
        var result = BoundingBoxNormalizer.Consolidate([
            new(0, 0, 5, 5), new(4, 4, 5, 5), new(8, 8, 4, 4), new(20, 20, 2, 2)]);
        result.ShouldBe([new PhysicalRectangle(0, 0, 12, 12), new PhysicalRectangle(20, 20, 2, 2)]);
    }

    [Fact]
    public void Consolidation_rejects_null_and_empty_input_regions()
    {
        Should.Throw<ArgumentNullException>(() => BoundingBoxNormalizer.Consolidate(null!));
        Should.Throw<ArgumentException>(() =>
            BoundingBoxNormalizer.Consolidate([new PhysicalRectangle(0, 0, 0, 1)]));
    }

    [Fact]
    public void Consolidation_keeps_regions_separated_in_each_direction()
    {
        var result = BoundingBoxNormalizer.Consolidate([
            new(10, 10, 2, 2),
            new(0, 10, 2, 2),
            new(10, 0, 2, 2),
            new(20, 10, 2, 2),
            new(10, 20, 2, 2)]);

        result.Count.ShouldBe(5);
    }

    [Property(MaxTest = 5000, QuietOnSuccess = true)]
    public bool Every_accepted_random_box_stays_inside_the_exact_image(
        PositiveInt widthValue, PositiveInt heightValue, int ax, int ay, int bx, int by, NonNegativeInt marginValue)
    {
        var width = Math.Clamp(widthValue.Get, 2, 4096);
        var height = Math.Clamp(heightValue.Get, 2, 4096);
        var x1 = Math.Min(ax % 8192, bx % 8192);
        var x2 = Math.Max(ax % 8192, bx % 8192);
        var y1 = Math.Min(ay % 8192, by % 8192);
        var y2 = Math.Max(ay % 8192, by % 8192);
        try
        {
            var crop = BoundingBoxNormalizer.Normalize(new(y1, x1, y2, x2), CoordinateSystem.Pixels,
                width, height, new(Math.Min(marginValue.Get, 100), 1, 1)).Pixels;
            return crop.X >= 0 && crop.Y >= 0 && crop.Right <= width && crop.Bottom <= height && !crop.IsEmpty;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
