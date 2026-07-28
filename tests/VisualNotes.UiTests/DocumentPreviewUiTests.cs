using Shouldly;

using VisualNotes.App.ViewModels;
using VisualNotes.Core.Services;

namespace VisualNotes.UiTests;

public sealed class DocumentPreviewUiTests
{
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
}
