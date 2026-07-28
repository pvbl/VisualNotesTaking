using NSubstitute;

using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

public sealed class PersistentRegionServiceTests
{
    [Fact, Trait("Category", "Unit")]
    public async Task Restore_preserves_physical_coordinates_dpi_monitor_and_current_session()
    {
        var store = Substitute.For<ISettingsStore>();
        var oldSession = Guid.NewGuid(); var currentSession = Guid.NewGuid();
        var monitor = new MonitorCaptureInfo("DISPLAY2", new(-1920, 0, 1920, 1080), 144, 144);
        var saved = new PersistentCaptureRegion(new(-1700, 100, 800, 600), monitor.DeviceName, 144, 144,
            oldSession, monitor.Bounds, IsLocked: true);
        store.GetAsync<PersistentCaptureRegion>(PersistentRegionService.ActiveRegionKey, Arg.Any<CancellationToken>()).Returns(saved);

        var result = await new PersistentRegionService(store).RestoreAsync([monitor], currentSession);

        result.Status.ShouldBe(RegionRestoreStatus.Restored);
        result.Region!.Bounds.ShouldBe(saved.Bounds);
        result.Region.DpiX.ShouldBe(144u); result.Region.MonitorDeviceName.ShouldBe("DISPLAY2");
        result.Region.SessionId.ShouldBe(currentSession); result.Region.IsLocked.ShouldBeTrue();
    }

    [Theory, Trait("Category", "Unit")]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(7, 100)]
    [InlineData(100, 7)]
    public async Task Save_rejects_invalid_coordinates(int width, int height)
    {
        var service = new PersistentRegionService(Substitute.For<ISettingsStore>());
        var region = new PersistentCaptureRegion(new(0, 0, width, height), "DISPLAY1", 96, 96,
            Guid.NewGuid(), new(0, 0, 1920, 1080));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => service.SaveAsync(region));
    }

    [Fact, Trait("Category", "Unit")]
    public async Task Named_favorite_replaces_name_case_insensitively()
    {
        var store = Substitute.For<ISettingsStore>();
        store.GetAsync<List<FavoriteCaptureRegion>>(PersistentRegionService.FavoritesKey, Arg.Any<CancellationToken>())
            .Returns(new List<FavoriteCaptureRegion>());
        var service = new PersistentRegionService(store);
        var region = new PersistentCaptureRegion(new(10, 10, 200, 100), "D", 96, 96, Guid.NewGuid(), new(0, 0, 800, 600));
        await service.SaveFavoriteAsync("Diapositivas", region);
        await store.Received().SetAsync(PersistentRegionService.FavoritesKey,
            Arg.Is<List<FavoriteCaptureRegion>>(x => x.Single().Name == "Diapositivas"), Arg.Any<CancellationToken>());
    }
}
