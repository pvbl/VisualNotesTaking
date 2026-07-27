namespace VisualNotes.Core.Models;

public sealed record CaptureRegion(int X, int Y, int Width, int Height);

public sealed record CapturedFrame(
    Guid Id,
    DateTimeOffset CapturedAt,
    CaptureRegion Region,
    byte[] ImageData,
    string MediaType = "image/png");

public sealed record ExtractedContent(
    string Text,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record NoteSection(
    Guid Id,
    string Title,
    string Content,
    int Order);

public sealed record ExportDocument(
    string Title,
    IReadOnlyList<NoteSection> Sections,
    DateTimeOffset CreatedAt);

public sealed class NoteSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Nueva sesión";
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? EndedAt { get; set; }
    public bool IsPaused { get; set; }
    public IList<CapturedFrame> Frames { get; } = new List<CapturedFrame>();
    public IList<NoteSection> Sections { get; } = new List<NoteSection>();
}
