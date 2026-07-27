namespace VisualNotes.Core.Models;

public enum EntityStatus { Active, Archived, Deleted }
public enum ScreenshotStatus { Captured, Queued, Analyzing, NeedsReview, Ready, Excluded, Failed }
public enum AnalysisJobStatus { Pending, Running, Completed, Failed, Cancelled }
public enum SessionProcessingStatus { Pending, Processing, Ready, Failed }

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public long Version { get; set; } = 1;
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}

public sealed class Course : Entity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ICollection<NoteSession> Sessions { get; set; } = [];
}

public sealed class NoteSession : Entity
{
    public Guid? CourseId { get; set; }
    public Course? Course { get; set; }
    public string Name { get; set; } = "Nueva sesión";
    public string Module { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string? Professor { get; set; }
    public string Language { get; set; } = "Español";
    public string WorkingFolder { get; set; } = string.Empty;
    public string PlannedDocumentName { get; set; } = string.Empty;
    public string InstructionTemplate { get; set; } = string.Empty;
    public Guid? ActiveSectionId { get; set; }
    public SessionProcessingStatus ProcessingStatus { get; set; }
    public DateTimeOffset? LastExportedAt { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public bool IsPaused { get; set; }
    public ICollection<Screenshot> Screenshots { get; set; } = [];
    public ICollection<NoteSection> Sections { get; set; } = [];
    [Obsolete("Use Screenshots instead.")]
    public IEnumerable<CapturedFrame> Frames => Screenshots.Select(x => new CapturedFrame(x.Id, x.CapturedAt, new CaptureRegion(0, 0, x.Width, x.Height), [], x.Image?.MediaType ?? "image/png"));
}

public sealed class NoteSection : Entity
{
    public NoteSection() { }
    public NoteSection(Guid id, string title, string content, int order) { Id = id; Title = title; Content = content; Order = order; }
    public Guid SessionId { get; set; }
    public NoteSession? Session { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid? ParentSectionId { get; set; }
    public NoteSection? ParentSection { get; set; }
    public ICollection<NoteSection> Children { get; set; } = [];
    public int Order { get; set; }
    public ICollection<Screenshot> Screenshots { get; set; } = [];
    public ICollection<GeneratedNote> GeneratedNotes { get; set; } = [];
}

public sealed class Screenshot : Entity
{
    public Guid SessionId { get; set; }
    public NoteSession? Session { get; set; }
    public Guid? SectionId { get; set; }
    public NoteSection? Section { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public ScreenshotStatus ProcessingStatus { get; set; } = ScreenshotStatus.Captured;
    public int Width { get; set; }
    public int Height { get; set; }
    public string? PerceptualHash { get; set; }
    public ScreenshotImage? Image { get; set; }
    public ScreenshotContext? Context { get; set; }
    public ICollection<AnalysisJob> AnalysisJobs { get; set; } = [];
}

public sealed class ScreenshotImage : Entity
{
    public Guid ScreenshotId { get; set; }
    public Screenshot? Screenshot { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string MediaType { get; set; } = "image/png";
    public long ByteLength { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class ScreenshotContext : Entity
{
    public Guid ScreenshotId { get; set; }
    public Screenshot? Screenshot { get; set; }
    public string? WindowTitle { get; set; }
    public string? ApplicationName { get; set; }
    public string? SourceUri { get; set; }
    public string? MetadataJson { get; set; }
}

public sealed class AnalysisJob : Entity
{
    public Guid ScreenshotId { get; set; }
    public Screenshot? Screenshot { get; set; }
    public Guid? PromptProfileId { get; set; }
    public Guid? ProviderProfileId { get; set; }
    public AnalysisJobStatus JobStatus { get; set; } = AnalysisJobStatus.Pending;
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public CaptureAnalysis? Result { get; set; }
}

public sealed class CaptureAnalysis : Entity
{
    public Guid AnalysisJobId { get; set; }
    public AnalysisJob? AnalysisJob { get; set; }
    public string ExtractedText { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? RawResultRelativePath { get; set; }
    public ICollection<VisualRegion> Regions { get; set; } = [];
}

public sealed class VisualRegion : Entity
{
    public Guid CaptureAnalysisId { get; set; }
    public CaptureAnalysis? CaptureAnalysis { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Label { get; set; }
    public string? CropRelativePath { get; set; }
}

public sealed class GeneratedNote : Entity
{
    public Guid SectionId { get; set; }
    public NoteSection? Section { get; set; }
    public Guid? CaptureAnalysisId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Format { get; set; } = "markdown";
}

public sealed class PromptProfile : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public sealed class ProviderProfile : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public string? ApiKeyReference { get; set; }
}

public sealed class ExportRecord : Entity
{
    public Guid SessionId { get; set; }
    public string Format { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AppSetting : Entity
{
    public string Key { get; set; } = string.Empty;
    public string JsonValue { get; set; } = "null";
    public bool IsSecret { get; set; }
}

public sealed record CaptureRegion(int X, int Y, int Width, int Height);
public sealed record CapturedFrame(Guid Id, DateTimeOffset CapturedAt, CaptureRegion Region, byte[] ImageData, string MediaType = "image/png");
public sealed record ExtractedContent(string Text, IReadOnlyDictionary<string, string>? Metadata = null);
public sealed record ExportDocument(string Title, IReadOnlyList<NoteSection> Sections, DateTimeOffset CreatedAt);
