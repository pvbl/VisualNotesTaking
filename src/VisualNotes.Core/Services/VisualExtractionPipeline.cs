using System.Globalization;
using System.Text;
using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

public sealed record VisualSource(string Name, string MediaType, ReadOnlyMemory<byte> Content, int Width, int Height);
public sealed record NormalizedVisualSource(string Name, string MediaType, ReadOnlyMemory<byte> RgbaPixels, int Width, int Height);
public sealed record PersistedExtraction(string ExtractionId, string NormalizedJson, IReadOnlyList<PersistedCrop> Crops);
public sealed record PersistedCrop(string Label, PhysicalRectangle Pixels, string RelativePath);
public sealed record ExtractionPipelineResult(PersistedExtraction Extraction, string Note, StructuredAnalysisResponse Response);

public interface IVisualSourceNormalizer
{
    Task<NormalizedVisualSource> NormalizeAsync(VisualSource source, CancellationToken cancellationToken = default);
}

public interface IExtractionArtifactStore
{
    Task<string> SaveExtractionAsync(string normalizedJson, CancellationToken cancellationToken = default);
    Task<string> SaveCropAsync(string extractionId, string label, PhysicalRectangle pixels, ReadOnlyMemory<byte> rgbaPixels, CancellationToken cancellationToken = default);
    Task<PersistedExtraction> LoadExtractionAsync(string extractionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrates the deterministic pipeline around the non-deterministic VLM boundary. Persisted normalized
/// extraction is the source of truth, so note composition never needs to invoke vision a second time.
/// </summary>
public sealed class VisualExtractionPipeline(
    IVisualSourceNormalizer normalizer,
    IVisionLanguageModelProvider visionModel,
    IExtractionArtifactStore artifacts,
    BoundingBoxNormalizationOptions? boundingBoxOptions = null,
    string? extractionPrompt = null)
{
    public async Task<ExtractionPipelineResult> ExtractAsync(
        VisualSource source, LanguageModelOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var normalized = await normalizer.NormalizeAsync(source, cancellationToken).ConfigureAwait(false);
        ValidatePixels(normalized);

        var request = new VisionLanguageModelRequest(
            extractionPrompt ?? ExtractionPrompt, normalized.RgbaPixels, normalized.MediaType, options, ImageDetail.High);
        var modelResponse = await visionModel.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        if (modelResponse.IsPartial)
            throw new AnalysisResponseValidationException("The provider returned a partial extraction.");

        var validated = StructuredAnalysisResponseParser.Parse(modelResponse.Text, new(false));
        var extractionId = await artifacts.SaveExtractionAsync(validated.NormalizedJson, cancellationToken).ConfigureAwait(false);
        var crops = new List<PersistedCrop>();
        var normalizedRegions = validated.Value.Regions.Select(region => (region.Label, Result:
            BoundingBoxNormalizer.Normalize(region.Box, validated.Value.CoordinateSystem, normalized.Width, normalized.Height, boundingBoxOptions))).ToArray();
        var consolidated = BoundingBoxNormalizer.Consolidate(normalizedRegions.Select(region => region.Result.Pixels));
        foreach (var pixels in consolidated)
        {
            var crop = VisualRegionCropper.Crop(normalized.RgbaPixels.Span, normalized.Width, normalized.Height, pixels);
            var labels = normalizedRegions.Where(region => Overlaps(region.Result.Pixels, pixels)).Select(region => region.Label);
            var label = string.Join(" + ", labels);
            var path = await artifacts.SaveCropAsync(extractionId, label, pixels, crop, cancellationToken).ConfigureAwait(false);
            crops.Add(new(label, pixels, path));
        }

        var persisted = new PersistedExtraction(extractionId, validated.NormalizedJson, crops);
        var clippingWarnings = normalizedRegions.SelectMany(region => region.Result.Warnings).Distinct().ToArray();
        var response = clippingWarnings.Length == 0 ? validated.Value : validated.Value with
        {
            Warnings = validated.Value.Warnings.Concat(clippingWarnings).ToArray()
        };
        return new(persisted, ComposeNote(response, crops), response);
    }

    public async Task<string> RegenerateNoteAsync(string extractionId, CancellationToken cancellationToken = default)
    {
        var persisted = await artifacts.LoadExtractionAsync(extractionId, cancellationToken).ConfigureAwait(false);
        var response = StructuredAnalysisResponseParser.Parse(persisted.NormalizedJson, new(false)).Value;
        return ComposeNote(response, persisted.Crops);
    }

    public const string ExtractionPrompt = """
        Extrae el contenido de la imagen y devuelve únicamente el contrato JSON solicitado.
        Preserva exactamente código e indentación, números, nombres propios y fórmulas; no los corrijas ni reformules.
        Devuelve tablas como Markdown con encabezados, filas y columnas siempre que sea posible.
        Usa coordinateSystem=Normalized1000. Añade regions con cajas sólo para elementos con valor visual
        (gráficas, diagramas o imágenes), no para texto, código, fórmulas ni tablas estructurables.
        Incluye confidence de 0 a 1 y warnings explícitas para texto ilegible, contenido cortado o incertidumbre.
        Propiedades obligatorias: language, contentType, title, summary, transcription, code, equations, tables,
        coordinateSystem, regions, concepts, confidence, warnings.
        """;

    private static string ComposeNote(StructuredAnalysisResponse value, IReadOnlyList<PersistedCrop> crops)
    {
        var note = new StringBuilder().Append("# ").AppendLine(value.Title).AppendLine().AppendLine(value.Summary);
        if (!string.IsNullOrWhiteSpace(value.Transcription)) note.AppendLine().AppendLine(value.Transcription);
        AppendBlocks(note, "Código", value.Code, true);
        AppendBlocks(note, "Fórmulas", value.Equations, false);
        AppendBlocks(note, "Tablas", value.Tables, false);
        if (crops.Count > 0)
        {
            note.AppendLine().AppendLine("## Elementos visuales");
            foreach (var crop in crops) note.Append("- ![").Append(crop.Label).Append("](").Append(crop.RelativePath).AppendLine(")");
        }
        note.AppendLine().Append("Confianza: ").Append(value.Confidence.ToString("0.###", CultureInfo.InvariantCulture));
        if (value.Warnings.Count > 0)
        {
            note.AppendLine().AppendLine().AppendLine("## Advertencias");
            foreach (var warning in value.Warnings) note.Append("- ").AppendLine(warning);
        }
        return note.ToString();
    }

    private static void AppendBlocks(StringBuilder note, string heading, IReadOnlyList<ContentBlock> blocks, bool fenced)
    {
        if (blocks.Count == 0) return;
        note.AppendLine().Append("## ").AppendLine(heading);
        foreach (var block in blocks)
        {
            if (fenced) note.AppendLine("```");
            note.AppendLine(block.Content);
            if (fenced) note.AppendLine("```");
        }
    }

    private static void ValidatePixels(NormalizedVisualSource source)
    {
        if (source.Width <= 0 || source.Height <= 0 || source.RgbaPixels.Length != checked(source.Width * source.Height * 4))
            throw new ArgumentException("Normalizer must return tightly packed RGBA pixels with positive dimensions.", nameof(source));
    }

    private static bool Overlaps(PhysicalRectangle first, PhysicalRectangle second) =>
        first.X < second.Right && second.X < first.Right && first.Y < second.Bottom && second.Y < first.Bottom;
}

public static class CoordinateConverter
{
    public static PhysicalRectangle ToPixels(BoundingBox box, CoordinateSystem system, int width, int height)
    {
        return BoundingBoxNormalizer.Normalize(box, system, width, height, new(MinimumWidth: 1, MinimumHeight: 1)).Pixels;
    }
}
