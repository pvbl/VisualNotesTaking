using System.Collections.ObjectModel;
using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

/// <summary>The semantic shapes supported by a composed study document.</summary>
public enum SemanticNodeType
{
    Section,
    Heading,
    Paragraph,
    List,
    Code,
    Equation,
    Table,
    Image,
    Context,
    Question
}

/// <summary>Identifies who supplied a node, so observed material is never presented as generated fact.</summary>
public enum SemanticContentOrigin { Observed, StudentComment, GeneratedExplanation }

/// <summary>A durable reference back to the capture from which content was obtained.</summary>
public sealed record CaptureSourceReference(Guid ScreenshotId, Guid? AnalysisId = null, BoundingBox? Region = null);

/// <summary>
/// A typed node in the note tree. <see cref="StableKey"/> identifies the same logical node across
/// regeneration; source references remain attached instead of being flattened into display text.
/// </summary>
public sealed record SemanticNode(
    string StableKey,
    SemanticNodeType Type,
    SemanticContentOrigin Origin,
    string Content,
    IReadOnlyList<CaptureSourceReference>? Sources = null,
    IReadOnlyList<SemanticNode>? Children = null)
{
    public IReadOnlyList<CaptureSourceReference> SourceReferences { get; init; } = Sources ?? [];
    public IReadOnlyList<SemanticNode> Nodes { get; init; } = Children ?? [];
}

public sealed record CaptureSemanticContent(
    Guid ScreenshotId,
    Guid SectionId,
    DateTimeOffset CapturedAt,
    IReadOnlyList<SemanticNode> Nodes,
    string StudentContext = "");

public sealed record SemanticDocument(string Title, IReadOnlyList<SemanticNode> Sections);

/// <summary>Deterministically composes capture results into a typed, provenance-aware tree.</summary>
public sealed class SemanticDocumentComposer
{
    public SemanticDocument Compose(NoteSession session, IEnumerable<CaptureSemanticContent> captures)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(captures);

        var included = captures
            .GroupBy(x => x.ScreenshotId)
            .Select(x => x.Last())
            .OrderBy(x => x.CapturedAt)
            .ThenBy(x => x.ScreenshotId)
            .ToArray();
        var sections = new List<SemanticNode>();

        foreach (var section in session.Sections.OrderBy(x => x.Order).ThenBy(x => x.Id))
        {
            var children = new List<SemanticNode>();
            foreach (var capture in included.Where(x => x.SectionId == section.Id))
            {
                if (!string.IsNullOrWhiteSpace(capture.StudentContext))
                    children.Add(new($"student:{capture.ScreenshotId}", SemanticNodeType.Context,
                        SemanticContentOrigin.StudentComment, capture.StudentContext,
                        [new(capture.ScreenshotId)]));

                foreach (var node in capture.Nodes)
                    children.Add(AttachSource(node, capture.ScreenshotId));
            }

            children = DistinctStableNodes(children);
            sections.Add(new($"section:{section.Id}", SemanticNodeType.Section,
                SemanticContentOrigin.Observed, section.Title, [], new ReadOnlyCollection<SemanticNode>(children)));
        }

        return new(session.Name, new ReadOnlyCollection<SemanticNode>(sections));
    }

    private static SemanticNode AttachSource(SemanticNode node, Guid screenshotId)
    {
        var sources = node.SourceReferences.Count == 0
            ? new[] { new CaptureSourceReference(screenshotId) }
            : node.SourceReferences;
        var children = node.Nodes.Select(x => AttachSource(x, screenshotId)).ToArray();
        return node with { SourceReferences = sources, Nodes = children };
    }

    private static List<SemanticNode> DistinctStableNodes(IEnumerable<SemanticNode> nodes)
    {
        var result = new List<SemanticNode>();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var identity = $"{node.Origin}:{node.StableKey}";
            if (positions.TryGetValue(identity, out var position))
                result[position] = node; // a regenerated logical node replaces its predecessor in place
            else
            {
                positions.Add(identity, result.Count);
                result.Add(node);
            }
        }
        return result;
    }
}

/// <summary>
/// Owns composition inputs and exposes regeneration at capture, section, and session granularity.
/// Student context is stored independently and is therefore preserved when generated content changes.
/// </summary>
public sealed class SemanticDocumentWorkspace(NoteSession session, SemanticDocumentComposer? composer = null)
{
    private readonly Dictionary<Guid, CaptureSemanticContent> _captures = [];
    private readonly SemanticDocumentComposer _composer = composer ?? new();

    public SemanticDocument Document => _composer.Compose(session, _captures.Values);

    public void RegenerateCapture(CaptureSemanticContent content) => _captures[content.ScreenshotId] =
        PreserveStudentContext(content, _captures.GetValueOrDefault(content.ScreenshotId));

    public void RegenerateSection(Guid sectionId, IEnumerable<CaptureSemanticContent> contents)
    {
        var previous = new Dictionary<Guid, CaptureSemanticContent>(_captures);
        foreach (var id in _captures.Values.Where(x => x.SectionId == sectionId).Select(x => x.ScreenshotId).ToArray())
            _captures.Remove(id);
        foreach (var content in contents)
            _captures[content.ScreenshotId] = PreserveStudentContext(content, previous.GetValueOrDefault(content.ScreenshotId));
    }

    public void RegenerateSession(IEnumerable<CaptureSemanticContent> contents)
    {
        var previous = new Dictionary<Guid, CaptureSemanticContent>(_captures);
        _captures.Clear();
        foreach (var content in contents)
            _captures[content.ScreenshotId] = PreserveStudentContext(content, previous.GetValueOrDefault(content.ScreenshotId));
    }

    private static CaptureSemanticContent PreserveStudentContext(CaptureSemanticContent current, CaptureSemanticContent? previous) =>
        string.IsNullOrWhiteSpace(current.StudentContext) && previous is not null
            ? current with { StudentContext = previous.StudentContext }
            : current;
}
