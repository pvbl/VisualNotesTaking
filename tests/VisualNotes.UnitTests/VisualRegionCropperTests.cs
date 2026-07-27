using Shouldly;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

[Trait("Category", "Unit")]
public sealed class VisualRegionCropperTests
{
    // Golden RGBA image: the center two pixels from a stable three-pixel red/green/blue row.
    private static readonly byte[] GoldenCrop = [0, 255, 0, 255, 0, 0, 255, 255];

    [Fact]
    public void Generated_crop_matches_golden_image_byte_for_byte()
    {
        byte[] source = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255];
        VisualRegionCropper.Crop(source, 3, 1, new PhysicalRectangle(1, 0, 2, 1)).ShouldBe(GoldenCrop);
    }

    [Theory]
    [InlineData(-1, 0, 1, 1)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(2, 0, 2, 1)]
    public void Rejects_invalid_crop_bounds(int x, int y, int width, int height) =>
        Should.Throw<ArgumentOutOfRangeException>(() => VisualRegionCropper.Crop(new byte[12], 3, 1, new(x, y, width, height)));
}
