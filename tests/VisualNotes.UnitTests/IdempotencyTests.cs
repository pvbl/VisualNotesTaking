using Shouldly;
using System.Text.Json;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using Xunit;

namespace VisualNotes.UnitTests;

[Trait("Category", "Unit")]
public sealed class IdempotencyTests
{
    [Fact]
    public void Repeating_box_consolidation_does_not_change_the_result()
    {
        PhysicalRectangle[] input = [new(0, 0, 5, 5), new(3, 3, 5, 5), new(20, 20, 2, 2)];
        var once = BoundingBoxNormalizer.Consolidate(input);
        BoundingBoxNormalizer.Consolidate(once).ShouldBe(once);
    }

    [Fact]
    public void Repeating_capture_regeneration_replaces_instead_of_duplicating_content()
    {
        var sectionId = Guid.NewGuid();
        var screenshotId = Guid.NewGuid();
        var session = new NoteSession { Sections = [new NoteSection { Id = sectionId, Title = "Tema" }] };
        var workspace = new SemanticDocumentWorkspace(session);
        var capture = new CaptureSemanticContent(screenshotId, sectionId, DateTimeOffset.UnixEpoch,
            [new("stable", SemanticNodeType.Paragraph, SemanticContentOrigin.Observed, "valor")]);

        workspace.RegenerateCapture(capture);
        var once = workspace.Document;
        workspace.RegenerateCapture(capture);

        JsonSerializer.Serialize(workspace.Document).ShouldBe(JsonSerializer.Serialize(once));
        workspace.Document.Sections.Single().Nodes.Count.ShouldBe(1);
    }
}
