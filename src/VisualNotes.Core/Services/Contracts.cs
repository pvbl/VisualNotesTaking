using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

public interface ICaptureWorkspace
{
    Task<IReadOnlyList<Screenshot>> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyCollection<Screenshot> captures, CancellationToken cancellationToken = default);
    Task<SemanticDocument> ComposeAsync(NoteSession session, CancellationToken cancellationToken = default);
}

public interface IDocumentExporter
{
    Task<string> ExportAsync(SemanticDocument document, string destinationPath, CancellationToken cancellationToken = default);
}

public interface IAnalysisJobProcessor
{
    Task<AnalysisJob> EnqueueAsync(AnalysisJob job, CancellationToken cancellationToken = default);
    Task<int> RunManualAsync(CancellationToken cancellationToken = default);
    Task<int> RunBatchAsync(Guid? sessionId = null, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task RetryAsync(Guid jobId, CancellationToken cancellationToken = default);
}

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

public interface ISettingsStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);
}

/// <summary>Purpose-specific API credentials. Implementations must use an OS-protected store.</summary>
public interface IApiCredentialStore
{
    Task SaveAsync(ApiCredentialProfile profile, string credential, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default);
    Task<string?> GetAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default);
    Task<bool> VerifyAsync(ApiCredentialProfile profile, string candidate, CancellationToken cancellationToken = default);
    Task DeleteAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default);
    Task<string?> GetMaskedAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default);
}

public enum ApiCredentialProfile { Extraction, Composition }

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
