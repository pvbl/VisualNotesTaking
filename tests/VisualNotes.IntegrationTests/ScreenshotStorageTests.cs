using Shouldly;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.Persistence;

namespace VisualNotes.IntegrationTests;

public sealed class ScreenshotStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VisualNotes-存储-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task Golden_resize_and_encoding_are_within_tolerance()
    {
        var bytes = CreateImage(3840, 2160);
        var result = await new ScreenshotStorageService(_root, 0).StoreAsync(new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), new MemoryStream(bytes)));
        result.Optimized.FinalWidth.ShouldBe(2560); result.Optimized.FinalHeight.ShouldBe(1440);
        result.Optimized.Scale.ShouldBe(2d / 3d, 0.0001); result.Optimized.Quality.ShouldBe(88);
        using var decoded = await Image.LoadAsync<Rgba32>(Path.Combine(_root, result.Optimized.RelativePath));
        var pixel = decoded[100, 100]; pixel.R.ShouldBeInRange((byte)115, (byte)125); pixel.G.ShouldBeInRange((byte)75, (byte)85); pixel.B.ShouldBeInRange((byte)35, (byte)45);
    }

    [Fact]
    public async Task Small_images_are_not_enlarged_and_thumbnail_is_independent()
    {
        var result = await Store(CreateImage(200, 100));
        result.Optimized.FinalWidth.ShouldBe(200); result.Thumbnail.FinalWidth.ShouldBe(200);
        result.Original.RelativePath.ShouldContain("/originals/"); result.Thumbnail.RelativePath.ShouldContain("/thumbnails/");
        var sessionRoot = Directory.GetDirectories(Path.Combine(_root, "sessions")).Single();
        Directory.GetDirectories(sessionRoot).Select(Path.GetFileName).ShouldBe(new[] { "analysis", "crops", "exports", "optimized", "originals", "thumbnails" }, ignoreOrder: true);
    }

    [Fact]
    public async Task Corrupt_input_leaves_no_committed_files()
    {
        await Should.ThrowAsync<UnknownImageFormatException>(() => Store([1, 2, 3, 4]));
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancellation_leaves_no_temporary_files()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => new ScreenshotStorageService(_root, 0).StoreAsync(new(Guid.NewGuid(), Guid.NewGuid(), new MemoryStream(CreateImage(100, 100))), cancellation.Token));
        Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Unicode_root_and_long_but_supported_path_are_handled()
    {
        var longRoot = Path.Combine(_root, new string('路', 80));
        var result = await new ScreenshotStorageService(longRoot, 0).StoreAsync(new(Guid.NewGuid(), Guid.NewGuid(), new MemoryStream(CreateImage(64, 32)), ScreenshotContentKind.CodeOrSmallText));
        result.Optimized.Format.ShouldBe("PNG"); File.Exists(Path.Combine(longRoot, result.Optimized.RelativePath)).ShouldBeTrue();
    }

    [Fact]
    public async Task Full_disk_is_reported_before_any_image_is_committed()
    {
        var service = new ScreenshotStorageService(_root, long.MaxValue);
        var exception = await Should.ThrowAsync<IOException>(() => service.StoreAsync(new(Guid.NewGuid(), Guid.NewGuid(), new MemoryStream(CreateImage(64, 32)))));
        exception.Message.ShouldContain("disk space");
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_storage_root_that_disappeared_into_a_file_reports_the_io_failure()
    {
        Directory.CreateDirectory(_root);
        var invalidRoot = Path.Combine(_root, "missing-path");
        await File.WriteAllTextAsync(invalidRoot, "not a directory");
        await Should.ThrowAsync<IOException>(() => new ScreenshotStorageService(invalidRoot, 0).StoreAsync(new(Guid.NewGuid(), Guid.NewGuid(), new MemoryStream(CreateImage(32, 32)))));
    }

    private Task<StoredScreenshot> Store(byte[] bytes) => new ScreenshotStorageService(_root, 0).StoreAsync(new(Guid.NewGuid(), Guid.NewGuid(), new MemoryStream(bytes)));
    private static byte[] CreateImage(int width, int height) { using var image = new Image<Rgba32>(width, height, new Rgba32(120, 80, 40)); using var stream = new MemoryStream(); image.SaveAsPng(stream); return stream.ToArray(); }
}
