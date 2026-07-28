using Shouldly;
using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

public sealed class DocumentPreviewTests
{
    [Fact]
    public void Selection_exclusion_and_order_are_applied_without_mutating_semantic_source()
    {
        var source = Document("uno", "dos", "tres");
        var preview = new SemanticDocumentPreview(source);
        preview.SetIncluded(["dos"], false);
        preview.Move("tres", 0);
        preview.Items.Single(item => item.StableKey == "uno").IsSelected = true;
        preview.Items.Single(item => item.StableKey == "tres").IsSelected = true;

        var exported = preview.CreateDocument(ExportScope.Selection);

        exported.Sections.Single().Nodes.Select(node => node.StableKey).ShouldBe(["tres", "uno"]);
        source.Sections.Single().Nodes.Select(node => node.StableKey).ShouldBe(["uno", "dos", "tres"]);
    }

    [Fact]
    public void Change_detection_covers_content_and_exported_file_changes()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "export");
            var document = Document("uno");
            var file = new FileInfo(path);
            var record = new ExportRecord(1, path, "{}", ExportChangeDetector.Fingerprint(document),
                DateTimeOffset.UtcNow, file.Length, file.LastWriteTimeUtc);
            ExportChangeDetector.HasChanges(document, record).ShouldBeFalse();
            ExportChangeDetector.HasChanges(Document("cambiado"), record).ShouldBeTrue();
            File.AppendAllText(path, " externo");
            ExportChangeDetector.ExportedFileChanged(record).ShouldBeTrue();
        }
        finally { File.Delete(path); }
    }

    private static SemanticDocument Document(params string[] keys) => new("Notas",
    [
        new("section", SemanticNodeType.Section, SemanticContentOrigin.Observed, "Tema", [],
            keys.Select(key => new SemanticNode(key, SemanticNodeType.Paragraph, SemanticContentOrigin.Observed, key)).ToArray())
    ]);
}
