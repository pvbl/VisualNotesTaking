using Shouldly;

using VisualNotes.App.ViewModels;
using VisualNotes.Core.Services;

namespace VisualNotes.UiTests;

public sealed class DocumentPreviewUiTests
{
    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Edited_content_is_exported_and_survives_preview_recomposition()
    {
        var original = Document("Texto original");
        var viewModel = new DocumentViewModel(original);
        var paragraph = viewModel.Items.Single(item => item.StableKey == "paragraph");

        paragraph.Content = "Texto escrito por el usuario";
        viewModel.ReplaceDocument(Document("Texto regenerado"));

        var edited = viewModel.Items.Single(item => item.StableKey == "paragraph");
        edited.Content.ShouldBe("Texto escrito por el usuario");
        edited.IsContentEdited.ShouldBeTrue();
        viewModel.CreateExportDocument().Sections.Single().Nodes.Single().Content
            .ShouldBe("Texto escrito por el usuario");
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Document_view_uses_an_editable_text_box_for_content()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VisualNotes.App", "Views", "DocumentView.xaml");
        var xaml = File.ReadAllText(Path.GetFullPath(path));

        xaml.ShouldContain("Text=\"{Binding Content, UpdateSourceTrigger=PropertyChanged}\"");
        xaml.ShouldNotContain("DisplayMemberBinding=\"{Binding Node.Content}\"");
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Preview_displays_warnings_and_exports_only_selection()
    {
        var document = new SemanticDocument("Notas",
        [
            new("section", SemanticNodeType.Section, SemanticContentOrigin.Observed, "Tema", [],
            [
                new("ready", SemanticNodeType.Paragraph, SemanticContentOrigin.Observed, "Listo"),
                new("missing", SemanticNodeType.Image, SemanticContentOrigin.Observed, "Archivo ausente")
            ])
        ]);
        var viewModel = new DocumentViewModel(document,
            new Dictionary<string, PreviewContentState> { ["missing"] = PreviewContentState.MissingFile });
        viewModel.Scope = ExportScope.Selection;
        viewModel.Items.Single(item => item.StableKey == "ready").IsSelected = true;
        SemanticDocument? requested = null;
        viewModel.ExportRequested += (export, _) => { requested = export; return Task.CompletedTask; };

        viewModel.ExportCommand.Execute(null);

        viewModel.HasWarnings.ShouldBeTrue();
        requested.ShouldNotBeNull().Sections.Single().Nodes.Single().StableKey.ShouldBe("ready");
    }

    private static SemanticDocument Document(string content) => new("Notas",
    [
        new("section", SemanticNodeType.Section, SemanticContentOrigin.Observed, "Tema", [],
        [
            new("paragraph", SemanticNodeType.Paragraph, SemanticContentOrigin.Observed, content)
        ])
    ]);
}
