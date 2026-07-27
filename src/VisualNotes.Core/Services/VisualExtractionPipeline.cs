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
    IExtractionArtifactStore artifacts)
{
    public async Task<ExtractionPipelineResult> ExtractAsync(
        VisualSource source, LanguageModelOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var normalized = await normalizer.NormalizeAsync(source, cancellationToken).ConfigureAwait(false);
        ValidatePixels(normalized);

        var request = new VisionLanguageModelRequest(
            ExtractionPrompt, normalized.RgbaPixels, normalized.MediaType, options, ImageDetail.High);
        var modelResponse = await visionModel.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        if (modelResponse.IsPartial)
            throw new AnalysisResponseValidationException("The provider returned a partial extraction.");

        var validated = StructuredAnalysisResponseParser.Parse(modelResponse.Text, new(false));
        var extractionId = await artifacts.SaveExtractionAsync(validated.NormalizedJson, cancellationToken).ConfigureAwait(false);
        var crops = new List<PersistedCrop>();
        foreach (var region in validated.Value.Regions)
        {
            var pixels = CoordinateConverter.ToPixels(region.Box, validated.Value.CoordinateSystem, normalized.Width, normalized.Height);
            var crop = SyntheticImageCropper.Crop(normalized.RgbaPixels.Span, normalized.Width, normalized.Height, pixels);
            var path = await artifacts.SaveCropAsync(extractionId, region.Label, pixels, crop, cancellationToken).ConfigureAwait(false);
            crops.Add(new(region.Label, pixels, path));
        }

        var persisted = new PersistedExtraction(extractionId, validated.NormalizedJson, crops);
        return new(persisted, ComposeNote(validated.Value, crops), validated.Value);
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
}

public static class CoordinateConverter
{
    public static PhysicalRectangle ToPixels(BoundingBox box, CoordinateSystem system, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var divisor = system switch { CoordinateSystem.Normalized01 => 1d, CoordinateSystem.Normalized1000 => 1000d, CoordinateSystem.Pixels => 0d, _ => throw new ArgumentOutOfRangeException(nameof(system)) };
        var x1 = system == CoordinateSystem.Pixels ? box.XMin : box.XMin / divisor * width;
        var y1 = system == CoordinateSystem.Pixels ? box.YMin : box.YMin / divisor * height;
        var x2 = system == CoordinateSystem.Pixels ? box.XMax : box.XMax / divisor * width;
        var y2 = system == CoordinateSystem.Pixels ? box.YMax : box.YMax / divisor * height;
        var left = Math.Clamp((int)Math.Floor(x1), 0, width - 1);
        var top = Math.Clamp((int)Math.Floor(y1), 0, height - 1);
        var right = Math.Clamp((int)Math.Ceiling(x2), left + 1, width);
        var bottom = Math.Clamp((int)Math.Ceiling(y2), top + 1, height);
        return new(left, top, right - left, bottom - top);
    }
}
