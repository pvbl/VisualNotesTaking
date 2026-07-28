using System.Text.Encodings.Web;
using System.Text.Json;

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

using Shouldly;

using VerifyXunit;

using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.Documents;

using Xunit;

namespace VisualNotes.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class OpenXmlDocumentExporterTests
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    [Fact]
    public async Task Exported_package_can_be_opened_and_walked()
    {
        var path = TemporaryPath();
        try
        {
            var result = await new OpenXmlDocumentExporter().ExportAsync(new(Document(), path, new()));
            result.ValidationErrors.ShouldBeEmpty();
            using var package = WordprocessingDocument.Open(path, false);
            new OpenXmlValidator().Validate(package).ShouldBeEmpty();
            var main = package.MainDocumentPart.ShouldNotBeNull();
            main.Document.Descendants().Count().ShouldBeGreaterThan(20);
            main.StyleDefinitionsPart.ShouldNotBeNull();
            main.HeaderParts.Count().ShouldBe(1);
            main.FooterParts.Count().ShouldBe(1);
            main.Document.InnerText.ShouldContain("Arquitectura");
            main.Document.InnerText.ShouldContain("dotnet test");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Open_xml_structure_snapshot_ignores_variable_relationship_ids()
    {
        var path = TemporaryPath();
        try
        {
            await new OpenXmlDocumentExporter().ExportAsync(new(Document(), path, new(Cover: false)));
            using var package = WordprocessingDocument.Open(path, false);
            var body = package.MainDocumentPart!.Document.Body!;
            var snapshot = JsonSerializer.Serialize(new
            {
                Parts = package.GetAllParts().Select(x => x.ContentType).Order().ToArray(),
                Elements = body.ChildElements.Select(x => x.LocalName).ToArray(),
                Styles = package.MainDocumentPart.StyleDefinitionsPart!.Styles!.Elements<DocumentFormat.OpenXml.Wordprocessing.Style>().Select(x => x.StyleId?.Value).ToArray(),
                Text = body.InnerText
            }, SnapshotJsonOptions);
            await Verifier.Verify(snapshot, extension: "json");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Existing_modified_file_requires_explicit_confirmation()
    {
        var path = TemporaryPath();
        try
        {
            await File.WriteAllTextAsync(path, "external change");
            var exception = await Should.ThrowAsync<IOException>(() => new OpenXmlDocumentExporter().ExportAsync(new(Document(), path, new())));
            exception.Message.ShouldContain("confirmación");
            (await File.ReadAllTextAsync(path)).ShouldBe("external change");

            await new OpenXmlDocumentExporter().ExportAsync(new(Document(), path, new(), ConfirmOverwrite: (_, _) => ValueTask.FromResult(true)));
            using var package = WordprocessingDocument.Open(path, false);
            package.MainDocumentPart.ShouldNotBeNull();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Export_records_version_path_configuration_and_detects_later_changes()
    {
        var path = TemporaryPath();
        try
        {
            var exporter = new OpenXmlDocumentExporter();
            var first = await exporter.ExportAsync(new(Document(), path, new(Cover: false)));
            var stored = await OpenXmlDocumentExporter.ReadRecordAsync(path);
            stored.ShouldNotBeNull();
            stored.Version.ShouldBe(1);
            stored.Path.ShouldBe(Path.GetFullPath(path));
            stored.Configuration.ShouldContain("Cover");
            ExportChangeDetector.HasChanges(Document(), stored).ShouldBeFalse();

            var second = await exporter.ExportAsync(new(Document(), path, new(Cover: false),
                ExpectedExistingVersion: ExportFileVersion.Read(path), PreviousExport: stored));
            second.Record.ShouldNotBeNull().Version.ShouldBe(2);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".visualnotes-export.json")) File.Delete(path + ".visualnotes-export.json");
        }
    }

    private static SemanticDocument Document() => new("Notas de prueba",
    [
        new("section:architecture", SemanticNodeType.Section, SemanticContentOrigin.Observed, "Arquitectura", [],
        [
            new("heading:components", SemanticNodeType.Heading, SemanticContentOrigin.Observed, "Componentes"),
            new("paragraph:description", SemanticNodeType.Paragraph, SemanticContentOrigin.GeneratedExplanation, "El paquete es independiente de Word."),
            new("code:test", SemanticNodeType.Code, SemanticContentOrigin.Observed, "dotnet test"),
            new("context:source", SemanticNodeType.Context, SemanticContentOrigin.StudentComment, "Contexto del estudiante"),
            new("table:parts", SemanticNodeType.Table, SemanticContentOrigin.Observed, "Parte | Propósito\nMain | Contenido")
        ])
    ]);

    private static string TemporaryPath() => Path.Combine(Path.GetTempPath(), $"visual-notes-{Guid.NewGuid():N}.docx");
}
