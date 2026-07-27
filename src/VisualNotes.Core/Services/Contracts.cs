using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

public interface IScreenCaptureService
{
    Task<CapturedFrame> CaptureAsync(CaptureRegion region, CancellationToken cancellationToken = default);
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
