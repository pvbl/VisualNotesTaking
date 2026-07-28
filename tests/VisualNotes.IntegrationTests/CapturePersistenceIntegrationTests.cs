using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Shouldly;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure;

namespace VisualNotes.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class CapturePersistenceIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VisualNotes-capture-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Deterministic_capture_is_stored_and_reloaded_from_sqlite()
    {
        var frame = await new DeterministicCaptureService().CaptureAsync(new(ScreenCaptureMode.CurrentMonitor));
        frame.ShouldNotBeNull();
        Guid screenshotId;
        string relativePath;

        await using (var runtime = await VisualNotesRuntime.CreateAsync(_root, CancellationToken.None))
        {
            var session = await runtime.Coordinator.CreateAsync(new NoteSession { Name = "Captura integrada" });
            var section = await runtime.Coordinator.AddSectionAsync(session, "Sección activa");
            await using var content = new MemoryStream(frame.ImageData, writable: false);
            var stored = await runtime.ScreenshotStorage.StoreAsync(new(session.Id, frame.Id, content, ScreenshotContentKind.CodeOrSmallText));
            var metadata = frame.Metadata!;
            var screenshot = new Screenshot
            {
                Id = frame.Id, SessionId = session.Id, SectionId = section.Id, CapturedAt = metadata.CapturedAt,
                Width = metadata.PixelWidth, Height = metadata.PixelHeight, PerceptualHash = stored.Optimized.Sha256,
                Image = new ScreenshotImage { ScreenshotId = frame.Id, RelativePath = stored.Optimized.RelativePath, MediaType = "image/png", ByteLength = stored.Optimized.Size, Sha256 = stored.Optimized.Sha256 },
                Context = new ScreenshotContext { ScreenshotId = frame.Id, MonitorDeviceName = metadata.MonitorDeviceName, CaptureMode = metadata.Mode, PhysicalX = metadata.PhysicalBounds.X, PhysicalY = metadata.PhysicalBounds.Y, DpiX = metadata.DpiX, DpiY = metadata.DpiY }
            };
            await runtime.Coordinator.AddCaptureAsync(session, screenshot);
            screenshotId = screenshot.Id;
            relativePath = stored.Optimized.RelativePath;
            File.Exists(Path.Combine(_root, relativePath)).ShouldBeTrue();
        }

        await using var restarted = await VisualNotesRuntime.CreateAsync(_root, CancellationToken.None);
        var reloaded = await restarted.Screenshots.GetAsync(screenshotId);
        reloaded.ShouldNotBeNull();
        reloaded.SectionId.ShouldNotBeNull();
        reloaded.Image.ShouldNotBeNull().RelativePath.ShouldBe(relativePath);
        reloaded.Image.Sha256.ShouldBe(reloaded.PerceptualHash);
        reloaded.Context.ShouldNotBeNull().MonitorDeviceName.ShouldBe("deterministic-monitor");
        reloaded.Context.DpiX.ShouldBe(144u);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class DeterministicCaptureService : IScreenCaptureService
    {
        public Task<CapturedFrame?> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default)
        {
            using var image = new Image<Rgba32>(8, 6, new Rgba32(20, 40, 60));
            using var stream = new MemoryStream();
            image.SaveAsPng(stream);
            var at = new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);
            var bounds = new PhysicalRectangle(10, 20, 8, 6);
            return Task.FromResult<CapturedFrame?>(new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), at,
                new(bounds.X, bounds.Y, bounds.Width, bounds.Height), stream.ToArray(), "image/png",
                new(request.Mode, bounds, at, "deterministic-monitor", null, null, 144, 144, 8, 6)));
        }
    }
}
