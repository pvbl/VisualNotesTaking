using NSubstitute;

using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.IntegrationTests;

public sealed class PersistentRegionResolutionTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task Simulated_resolution_change_clamps_region_onscreen()
    {
        var store = Substitute.For<ISettingsStore>();
        var saved = new PersistentCaptureRegion(new(1400, 700, 500, 350), "DISPLAY1", 96, 96,
            Guid.NewGuid(), new(0, 0, 1920, 1080));
        store.GetAsync<PersistentCaptureRegion>(PersistentRegionService.ActiveRegionKey, Arg.Any<CancellationToken>()).Returns(saved);
        var resized = new MonitorCaptureInfo("DISPLAY1", new(0, 0, 1280, 720), 120, 120, true);

        var result = await new PersistentRegionService(store).RestoreAsync([resized], Guid.NewGuid());

        result.Status.ShouldBe(RegionRestoreStatus.AdjustedForResolution);
        result.Region!.Bounds.ShouldBe(new PhysicalRectangle(780, 370, 500, 350));
        result.Region.DpiX.ShouldBe(120u);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Disconnected_monitor_moves_region_to_primary_monitor()
    {
        var store = Substitute.For<ISettingsStore>();
        var saved = new PersistentCaptureRegion(new(-1800, 20, 600, 400), "DISCONNECTED", 144, 144,
            Guid.NewGuid(), new(-1920, 0, 1920, 1080));
        store.GetAsync<PersistentCaptureRegion>(PersistentRegionService.ActiveRegionKey, Arg.Any<CancellationToken>()).Returns(saved);
        var primary = new MonitorCaptureInfo("PRIMARY", new(0, 0, 1366, 768), 96, 96, true);

        var result = await new PersistentRegionService(store).RestoreAsync([primary], Guid.NewGuid());

        result.Status.ShouldBe(RegionRestoreStatus.MovedToAvailableMonitor);
        ScreenCaptureGeometry.Intersect(result.Region!.Bounds, primary.Bounds).ShouldBe(result.Region.Bounds);
    }
}
