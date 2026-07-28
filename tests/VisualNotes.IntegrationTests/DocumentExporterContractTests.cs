using Shouldly;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.Documents;
using Xunit;

namespace VisualNotes.IntegrationTests;

/// <summary>Reusable behavioral contract. Every document exporter gets a derived fixture.</summary>
public abstract class DocumentExporterContractTests
{
    protected abstract Task<ExportObservation> ExportAsync(string path, CancellationToken cancellationToken);

    [Fact, Trait("Category", "Integration")]
    public async Task Export_creates_a_non_empty_artifact()
    {
        var path = TemporaryPath();
        try
        {
            var result = await ExportAsync(path, default);
            result.Path.ShouldBe(Path.GetFullPath(path));
            result.Bytes.ShouldBeGreaterThan(0);
            File.Exists(path).ShouldBeTrue();
        }
        finally { Delete(path); }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Export_honors_pre_cancelled_tokens_without_leaving_an_artifact()
    {
        var path = TemporaryPath();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await Should.ThrowAsync<OperationCanceledException>(() => ExportAsync(path, cancellation.Token));
            File.Exists(path).ShouldBeFalse();
        }
        finally { Delete(path); }
    }

    protected static SemanticDocument Document() => new("Contrato", [
        new("section:one", SemanticNodeType.Section, SemanticContentOrigin.Observed, "Uno", [], [
            new("paragraph:one", SemanticNodeType.Paragraph, SemanticContentOrigin.Observed, "Contenido")])]);

    private static string TemporaryPath() => Path.Combine(Path.GetTempPath(), $"visual-notes-contract-{Guid.NewGuid():N}.docx");
    private static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".visualnotes-export.json")) File.Delete(path + ".visualnotes-export.json");
    }

    protected sealed record ExportObservation(string Path, long Bytes);
}

public sealed class OpenXmlDocumentExporterContractTests : DocumentExporterContractTests
{
    protected override async Task<ExportObservation> ExportAsync(string path, CancellationToken cancellationToken)
    {
        var result = await new OpenXmlDocumentExporter().ExportAsync(new(Document(), path, new()), cancellationToken);
        return new(result.Path, result.Bytes);
    }
}
