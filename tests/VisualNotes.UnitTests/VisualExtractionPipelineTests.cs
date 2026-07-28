using System.Text.Json;

using NSubstitute;

using Shouldly;

using VisualNotes.Core.Services;

using Xunit;

namespace VisualNotes.UnitTests;

[Trait("Category", "Unit")]
public sealed class VisualExtractionPipelineTests
{
    [Theory]
    [InlineData(100, 200, 900, 800, 40, 20, 120, 160)]
    public void Normalized_coordinates_are_converted_outwards(
        double y1, double x1, double y2, double x2, int x, int y, int width, int height)
    {
        CoordinateConverter.ToPixels(new(y1, x1, y2, x2), CoordinateSystem.Normalized1000, 200, 200)
            .ShouldBe(new VisualNotes.Core.Models.PhysicalRectangle(x, y, width, height));
    }

    [Fact]
    public async Task Extraction_is_saved_and_note_can_be_regenerated_without_the_model()
    {
        var json = await File.ReadAllTextAsync(Fixture("cases/chart.json"));
        var pixels = new byte[20 * 10 * 4];
        var normalizer = Substitute.For<IVisualSourceNormalizer>();
        normalizer.NormalizeAsync(Arg.Any<VisualSource>(), Arg.Any<CancellationToken>())
            .Returns(new NormalizedVisualSource("chart", "image/rgba", pixels, 20, 10));
        var model = Substitute.For<IVisionLanguageModelProvider>();
        model.GenerateAsync(Arg.Any<VisionLanguageModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LanguageModelResponse(json, "fixture-vlm", new(null, null)));
        var store = new MemoryStore();
        var pipeline = new VisualExtractionPipeline(normalizer, model, store);

        var result = await pipeline.ExtractAsync(new("chart", "image/png", new byte[] { 1 }, 1, 1), new("fixture-vlm"));
        var regenerated = await pipeline.RegenerateNoteAsync(result.Extraction.ExtractionId);

        regenerated.ShouldBe(result.Note);
        result.Extraction.Crops.Count.ShouldBe(1);
        result.Note.ShouldContain("Confianza: 0.93");
        result.Note.ShouldContain("Advertencias");
        await model.Received(1).GenerateAsync(Arg.Any<VisionLanguageModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Prompt_requires_fidelity_structured_tables_and_visual_only_boxes()
    {
        VisualExtractionPipeline.ExtractionPrompt.ShouldContain("Preserva exactamente código e indentación");
        VisualExtractionPipeline.ExtractionPrompt.ShouldContain("tablas como Markdown");
        VisualExtractionPipeline.ExtractionPrompt.ShouldContain("cajas sólo para elementos con valor visual");
        VisualExtractionPipeline.ExtractionPrompt.ShouldContain("confidence");
        VisualExtractionPipeline.ExtractionPrompt.ShouldContain("warnings");
    }

    [Fact]
    public async Task Versioned_private_free_dataset_has_stable_normalized_responses()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Fixture("dataset.json")));
        manifest.RootElement.GetProperty("datasetVersion").GetString().ShouldBe("1.0.0");
        manifest.RootElement.GetProperty("privacy").GetString().ShouldNotBeNull().ShouldContain("Synthetic");
        var cases = manifest.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        cases.Length.ShouldBe(8);
        foreach (var item in cases)
        {
            var path = Fixture(item.GetProperty("expected").GetString()!);
            var first = StructuredAnalysisResponseParser.Parse(await File.ReadAllTextAsync(path), new(false));
            var second = StructuredAnalysisResponseParser.Parse(first.NormalizedJson, new(false));
            second.NormalizedJson.ShouldBe(first.NormalizedJson, item.GetProperty("id").GetString());
        }
    }

    private static string Fixture(string relative) => Path.Combine(AppContext.BaseDirectory, "Fixtures", relative);

    private sealed class MemoryStore : IExtractionArtifactStore
    {
        private PersistedExtraction? _saved;
        public Task<string> SaveExtractionAsync(string normalizedJson, CancellationToken cancellationToken = default)
        {
            _saved = new("extraction-1", normalizedJson, []);
            return Task.FromResult(_saved.ExtractionId);
        }
        public Task<string> SaveCropAsync(string extractionId, string label, VisualNotes.Core.Models.PhysicalRectangle pixels, ReadOnlyMemory<byte> rgbaPixels, CancellationToken cancellationToken = default)
        {
            var crop = new PersistedCrop(label, pixels, $"crops/{label}.rgba");
            _saved = _saved! with { Crops = [crop] };
            return Task.FromResult(crop.RelativePath);
        }
        public Task<PersistedExtraction> LoadExtractionAsync(string extractionId, CancellationToken cancellationToken = default) => Task.FromResult(_saved!);
    }
}
