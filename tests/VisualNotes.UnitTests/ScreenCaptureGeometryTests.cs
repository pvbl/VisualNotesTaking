using Shouldly;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

public sealed class ScreenCaptureGeometryTests
{
    [Theory]
    [InlineData(100, 96, 100)] [InlineData(100, 120, 125)] [InlineData(100, 144, 150)] [InlineData(100, 192, 200)]
    [Trait("Category", "Unit")]
    public void Dip_conversion_obeys_monitor_dpi(double dip, uint dpi, int expected) =>
        ScreenCaptureGeometry.DipToPhysical(dip, dpi).ShouldBe(expected);

    [Fact, Trait("Category", "Unit")]
    public void Monitor_offset_preserves_negative_virtual_coordinates()
    {
        var monitor = new MonitorCaptureInfo("LEFT", new(-1920, -200, 1920, 1080), 120, 144);
        ScreenCaptureGeometry.DipToPhysical(80, 100, 400, 200, monitor)
            .ShouldBe(new PhysicalRectangle(-1820, -50, 500, 300));
    }

    [Fact, Trait("Category", "Unit")]
    public void Reverse_drag_is_normalized() =>
        ScreenCaptureGeometry.Normalize(300, 200, -100, -50).ShouldBe(new PhysicalRectangle(-100, -50, 400, 250));
}
