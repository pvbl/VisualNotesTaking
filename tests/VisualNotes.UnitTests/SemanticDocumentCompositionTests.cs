using System.Text.Encodings.Web;
using System.Text.Json;

using Shouldly;

using VerifyXunit;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

public sealed class SemanticDocumentCompositionTests
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private static readonly Guid SectionId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid CaptureId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [Fact]
    public void Composition_preserves_section_capture_and_block_order()
    {
        var second = new NoteSection { Id = Guid.NewGuid(), Title = "Después", Order = 1 };
        var session = Session(new NoteSection { Id = SectionId, Title = "Primero", Order = 0 }, second);
        var late = Capture(CaptureId, SectionId, 2, new("late", SemanticNodeType.Paragraph, SemanticContentOrigin.Observed, "B"));
        var early = Capture(Guid.NewGuid(), SectionId, 1, new("early", SemanticNodeType.Heading, SemanticContentOrigin.Observed, "A"));

        var result = new SemanticDocumentComposer().Compose(session, [late, early]);

        result.Sections.Select(x => x.Content).ShouldBe(["Primero", "Después"]);
        result.Sections[0].Nodes.Select(x => x.Content).ShouldBe(["A", "B"]);
    }

    [Fact]
    public void Regeneration_replaces_nodes_without_duplication_and_keeps_source()
    {
        var workspace = new SemanticDocumentWorkspace(Session(new NoteSection { Id = SectionId, Title = "Tema" }));
        workspace.RegenerateCapture(Capture(CaptureId, SectionId, 1,
            new("definition", SemanticNodeType.Paragraph, SemanticContentOrigin.GeneratedExplanation, "vieja")));
        workspace.RegenerateCapture(Capture(CaptureId, SectionId, 1,
            new("definition", SemanticNodeType.Paragraph, SemanticContentOrigin.GeneratedExplanation, "nueva")));

        var nodes = workspace.Document.Sections.Single().Nodes;
        nodes.Count.ShouldBe(1);
        nodes.Single().Content.ShouldBe("nueva");
        nodes.Single().SourceReferences.Single().ScreenshotId.ShouldBe(CaptureId);
    }

    [Fact]
    public void Capture_section_and_session_regeneration_preserve_personal_context()
    {
        var workspace = new SemanticDocumentWorkspace(Session(new NoteSection { Id = SectionId, Title = "Tema" }));
        workspace.RegenerateCapture(Capture(CaptureId, SectionId, 1,
            new("explanation", SemanticNodeType.Paragraph, SemanticContentOrigin.GeneratedExplanation, "v1"), "Mi regla mnemotécnica"));

        workspace.RegenerateCapture(Capture(CaptureId, SectionId, 1,
            new("explanation", SemanticNodeType.Paragraph, SemanticContentOrigin.GeneratedExplanation, "v2")));
        workspace.RegenerateSection(SectionId, [Capture(CaptureId, SectionId, 1,
            new("explanation", SemanticNodeType.Paragraph, SemanticContentOrigin.GeneratedExplanation, "v3"))]);
        workspace.RegenerateSession([Capture(CaptureId, SectionId, 1,
            new("explanation", SemanticNodeType.Paragraph, SemanticContentOrigin.GeneratedExplanation, "v4"))]);

        var context = workspace.Document.Sections.Single().Nodes.Single(x => x.Origin == SemanticContentOrigin.StudentComment);
        context.Content.ShouldBe("Mi regla mnemotécnica");
        context.SourceReferences.Single().ScreenshotId.ShouldBe(CaptureId);
    }

    [Fact]
    public Task Semantic_tree_snapshot_is_reviewable()
    {
        var session = Session(new NoteSection { Id = SectionId, Title = "Derivadas", Order = 0 });
        var nodes = new SemanticNode[]
        {
            new("heading", SemanticNodeType.Heading, SemanticContentOrigin.Observed, "Regla de la cadena"),
            new("formula", SemanticNodeType.Equation, SemanticContentOrigin.Observed, "(f∘g)'=(f'∘g)g'"),
            new("why", SemanticNodeType.Paragraph, SemanticContentOrigin.GeneratedExplanation, "Deriva la función exterior y multiplica por la interior.")
        };
        var document = new SemanticDocumentComposer().Compose(session, [new(CaptureId, SectionId, DateTimeOffset.UnixEpoch, nodes, "Recordar el orden exterior → interior")]);
        var snapshot = JsonSerializer.Serialize(new
        {
            document.Title,
            Sections = document.Sections.Select(section => new
            {
                section.StableKey,
                Type = section.Type.ToString(),
                section.Content,
                Children = section.Nodes.Select(node => new
                {
                    node.StableKey,
                    Type = node.Type.ToString(),
                    Origin = node.Origin.ToString(),
                    node.Content,
                    Sources = node.SourceReferences.Select(source => source.ScreenshotId)
                })
            })
        }, SnapshotJsonOptions);
        return Verifier.Verify(snapshot, extension: "json");
    }

    private static CaptureSemanticContent Capture(Guid id, Guid section, int minute, SemanticNode node, string context = "") =>
        new(id, section, DateTimeOffset.UnixEpoch.AddMinutes(minute), [node], context);

    private static NoteSession Session(params NoteSection[] sections)
    {
        var session = new NoteSession { Id = Guid.Parse("30000000-0000-0000-0000-000000000001"), Name = "Cálculo" };
        foreach (var section in sections) session.Sections.Add(section);
        return session;
    }
}
