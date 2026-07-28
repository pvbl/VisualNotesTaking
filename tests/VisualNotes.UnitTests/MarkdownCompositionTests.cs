using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

public sealed class MarkdownCompositionTests
{
    [Fact]
    public void Review_markdown_preserves_section_order_metadata_and_inclusion()
    {
        var first = new NoteSection { Id = Guid.NewGuid(), Title = "Conceptos", Order = 0 };
        var second = new NoteSection { Id = Guid.NewGuid(), Title = "Ejemplos", Order = 1 };
        var session = new NoteSession { Name = "Álgebra", Sections = [second, first] };
        var included = new Screenshot
        {
            SectionId = first.Id,
            DisplayTitle = "Vectores",
            Tags = "definición, examen",
            UserContext = "Recordar la base canónica.",
            Image = new ScreenshotImage { RelativePath = "sessions/a/capture.png" }
        };
        var excluded = new Screenshot
        {
            SectionId = second.Id,
            DisplayTitle = "No enviar",
            IncludeInDocument = false
        };

        var markdown = MarkdownComposition.ComposeReview(session, [excluded, included]);

        markdown.ShouldContain("# Álgebra");
        markdown.ShouldContain("## Conceptos");
        markdown.ShouldContain("### Vectores");
        markdown.ShouldContain("**Etiquetas:** definición, examen");
        markdown.ShouldContain("![Vectores](sessions/a/capture.png)");
        markdown.ShouldNotContain("No enviar");
    }

    [Fact]
    public void Semantic_document_is_serialized_to_editable_markdown()
    {
        var document = new SemanticDocument("Cálculo",
        [
            new("section", SemanticNodeType.Section, SemanticContentOrigin.Observed, "Derivadas", [],
            [
                new("heading", SemanticNodeType.Heading, SemanticContentOrigin.Observed, "Regla"),
                new("code", SemanticNodeType.Code, SemanticContentOrigin.Observed, "f'(x) = 2x")
            ])
        ]);

        var markdown = MarkdownComposition.ComposeDocument(document);

        markdown.ShouldContain("# Cálculo");
        markdown.ShouldContain("## Derivadas");
        markdown.ShouldContain("### Regla");
        markdown.ShouldContain("```");
        markdown.ShouldContain("f'(x) = 2x");
    }
}
