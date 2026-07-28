using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.IntegrationTests;

public sealed class SyntheticCaptureTests
{
    [Fact, Trait("Category", "Integration")]
    public void Crop_has_expected_dimensions_colors_and_region()
    {
        const int width = 4, height = 3;
        var source = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                source[offset] = (byte)(x * 10); source[offset + 1] = (byte)(y * 20);
                source[offset + 2] = 77; source[offset + 3] = 255;
            }
        var result = SyntheticImageCropper.Crop(source, width, height, new PhysicalRectangle(1, 1, 2, 2));
        result.Length.ShouldBe(2 * 2 * 4);
        result[..4].ShouldBe(new byte[] { 10, 20, 77, 255 });
        result[^4..].ShouldBe(new byte[] { 20, 40, 77, 255 });
    }
}
