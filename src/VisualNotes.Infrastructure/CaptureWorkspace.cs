using Microsoft.EntityFrameworkCore;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.Persistence;

namespace VisualNotes.Infrastructure;

internal sealed class CaptureWorkspace(VisualNotesDbContext database, IScreenshotRepository screenshots) : ICaptureWorkspace
{
    public Task<IReadOnlyList<Screenshot>> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        screenshots.ListBySessionAsync(sessionId, cancellationToken);

    public async Task SaveAsync(IReadOnlyCollection<Screenshot> captures, CancellationToken cancellationToken = default)
    {
        foreach (var capture in captures)
        {
            var persisted = database.Screenshots.Local.FirstOrDefault(x => x.Id == capture.Id) ??
                await database.Screenshots.SingleAsync(x => x.Id == capture.Id, cancellationToken).ConfigureAwait(false);
            persisted.SectionId = capture.SectionId;
            persisted.Status = capture.Status;
            persisted.ProcessingStatus = capture.ProcessingStatus;
            persisted.IncludeInDocument = capture.IncludeInDocument;
            persisted.Tags = capture.Tags;
            persisted.UserContext = capture.UserContext;
            persisted.CaptureInstruction = capture.CaptureInstruction;
            persisted.ModifiedAt = DateTimeOffset.UtcNow;
        }
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SemanticDocument> ComposeAsync(NoteSession session, CancellationToken cancellationToken = default)
    {
        var captures = await LoadAsync(session.Id, cancellationToken).ConfigureAwait(false);
        var contents = captures.Where(x => x.Status != EntityStatus.Deleted && x.IncludeInDocument)
            .Select(ToSemanticContent).Where(x => x is not null).Cast<CaptureSemanticContent>();
        return new SemanticDocumentComposer().Compose(session, contents);
    }

    private static CaptureSemanticContent? ToSemanticContent(Screenshot capture)
    {
        if (capture.SectionId is not { } sectionId) return null;
        var analysis = capture.AnalysisJobs.Where(x => x.JobStatus == AnalysisJobStatus.Completed && x.Result is not null)
            .OrderBy(x => x.CompletedAt).LastOrDefault()?.Result;
        if (analysis is null) return null;
        var nodes = new List<SemanticNode>();
        if (!string.IsNullOrWhiteSpace(analysis.NormalizedResponseJson))
        {
            var response = StructuredAnalysisResponseParser.Parse(analysis.NormalizedResponseJson, new(false)).Value;
            if (!string.IsNullOrWhiteSpace(response.Title)) nodes.Add(Node("title", SemanticNodeType.Heading, response.Title, capture.Id, analysis.Id));
            if (!string.IsNullOrWhiteSpace(response.Transcription)) nodes.Add(Node("text", SemanticNodeType.Paragraph, response.Transcription, capture.Id, analysis.Id));
            nodes.AddRange(response.Code.Select((x, i) => Node($"code:{i}", SemanticNodeType.Code, x.Content, capture.Id, analysis.Id, x.Box)));
            nodes.AddRange(response.Equations.Select((x, i) => Node($"equation:{i}", SemanticNodeType.Equation, x.Content, capture.Id, analysis.Id, x.Box)));
            nodes.AddRange(response.Tables.Select((x, i) => Node($"table:{i}", SemanticNodeType.Table, x.Content, capture.Id, analysis.Id, x.Box)));
        }
        else if (!string.IsNullOrWhiteSpace(analysis.ExtractedText))
            nodes.Add(Node("text", SemanticNodeType.Paragraph, analysis.ExtractedText, capture.Id, analysis.Id));
        return new(capture.Id, sectionId, capture.CapturedAt, nodes, capture.UserContext);
    }

    private static SemanticNode Node(string key, SemanticNodeType type, string content, Guid screenshotId, Guid analysisId, BoundingBox? box = null) =>
        new($"capture:{screenshotId}:{key}", type, SemanticContentOrigin.Observed, content, [new(screenshotId, analysisId, box)]);
}
