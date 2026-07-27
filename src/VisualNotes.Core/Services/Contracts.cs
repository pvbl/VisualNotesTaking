using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

public interface IScreenCaptureService
{
    Task<CapturedFrame?> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default);
}

public interface IContentAnalysisService
{
    Task<ExtractedContent> AnalyzeAsync(CapturedFrame frame, CancellationToken cancellationToken = default);
}

public interface INoteComposer
{
    Task<NoteSection> ComposeAsync(ExtractedContent content, int order, CancellationToken cancellationToken = default);
}

public interface IDocumentExporter
{
    Task ExportAsync(ExportDocument document, Stream destination, CancellationToken cancellationToken = default);
}

public interface ISettingsStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);
}

public interface IScreenshotStorageService
{
    Task<StoredScreenshot> StoreAsync(ScreenshotStorageRequest request, CancellationToken cancellationToken = default);
}

public enum ScreenshotContentKind { VideoOrImage, CodeOrSmallText }
public enum ScreenshotSizePreset { Default2560, FullHd1920, Original, Custom }

public sealed record ScreenshotStorageRequest(
    Guid SessionId,
    Guid ScreenshotId,
    Stream Content,
    ScreenshotContentKind ContentKind = ScreenshotContentKind.VideoOrImage,
    ScreenshotSizePreset SizePreset = ScreenshotSizePreset.Default2560,
    int? CustomMaximumSide = null,
    int ThumbnailMaximumSide = 320);

public sealed record StoredImageMetadata(
    string RelativePath, int OriginalWidth, int OriginalHeight, int FinalWidth, int FinalHeight,
    double Scale, string Format, int? Quality, string Sha256, long Size);

public sealed record StoredScreenshot(StoredImageMetadata Original, StoredImageMetadata Optimized, StoredImageMetadata Thumbnail);
