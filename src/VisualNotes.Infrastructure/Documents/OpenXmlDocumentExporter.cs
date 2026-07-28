using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

using VisualNotes.Core.Services;

using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace VisualNotes.Infrastructure.Documents;

public enum ExportImageMode { FullCapture, SourceRegion }

public sealed record OpenXmlExportOptions(
    bool Cover = true, bool TableOfContents = true, bool Header = true,
    bool Footer = true, bool PageNumbers = true, ExportImageMode ImageMode = ExportImageMode.SourceRegion,
    string HeaderText = "Visual Notes", string FooterText = "Documento generado por Visual Notes");

public sealed record ExportFileVersion(long Length, DateTime LastWriteTimeUtc)
{
    public static ExportFileVersion Read(string path) => new(new FileInfo(path).Length, File.GetLastWriteTimeUtc(path));
}

public sealed record ExportImage(byte[] Content, string ContentType = "image/png");

public sealed record OpenXmlExportRequest(
    SemanticDocument Document, string DestinationPath, OpenXmlExportOptions Options,
    Func<Guid, CancellationToken, ValueTask<ExportImage?>>? ImageProvider = null,
    ExportFileVersion? ExpectedExistingVersion = null,
    Func<string, CancellationToken, ValueTask<bool>>? ConfirmOverwrite = null,
    ExportRecord? PreviousExport = null);

public sealed record OpenXmlExportResult(string Path, long Bytes, IReadOnlyList<string> ValidationErrors,
    ExportRecord? Record = null);

public enum ExportFailureKind { InvalidPath, FileLocked, DiskFull, AccessDenied, InputOutput }

public sealed class DocumentExportException(ExportFailureKind kind, string message, Exception innerException)
    : IOException(message, innerException)
{
    public ExportFailureKind Kind { get; } = kind;
}

/// <summary>Creates self-contained DOCX packages without automating or requiring Microsoft Word.</summary>
public sealed class OpenXmlDocumentExporter : IDocumentExporter
{
    private const long ImageWidth = 5_700_000;

    async Task<string> IDocumentExporter.ExportAsync(SemanticDocument document, string destinationPath, CancellationToken cancellationToken) =>
        (await ExportAsync(new(document, destinationPath, new()), cancellationToken).ConfigureAwait(false)).Path;

    public async Task<OpenXmlExportResult> ExportAsync(OpenXmlExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationPath);
        string destination;
        try
        {
            destination = Path.GetFullPath(request.DestinationPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await EnsureDestinationCanBeReplacedAsync(request, destination, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new DocumentExportException(ExportFailureKind.InvalidPath, "La ruta de exportación no es válida.", exception);
        }

        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await CreatePackageAsync(request, temporary, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<string> errors;
            using (var package = WordprocessingDocument.Open(temporary, false))
                errors = new OpenXmlValidator().Validate(package, CancellationToken.None).Select(x => $"{x.Path?.XPath}: {x.Description}").ToArray();
            if (errors.Count != 0)
                throw new InvalidDataException("El paquete Open XML no es válido: " + string.Join("; ", errors));

            ReplaceAtomically(temporary, destination);
            var file = new FileInfo(destination);
            var record = new ExportRecord((request.PreviousExport?.Version ?? 0) + 1, destination,
                System.Text.Json.JsonSerializer.Serialize(request.Options), ExportChangeDetector.Fingerprint(request.Document),
                DateTimeOffset.UtcNow, file.Length, file.LastWriteTimeUtc);
            await WriteRecordAsync(record, cancellationToken).ConfigureAwait(false);
            return new(destination, file.Length, errors, record);
        }
        catch (DocumentExportException) { throw; }
        catch (UnauthorizedAccessException exception)
        {
            throw new DocumentExportException(ExportFailureKind.AccessDenied, "No hay permisos para escribir en la ruta seleccionada.", exception);
        }
        catch (IOException exception)
        {
            var full = (exception.HResult & 0xFFFF) is 0x27 or 0x70;
            var locked = File.Exists(destination) && !CanOpenExclusively(destination);
            throw new DocumentExportException(full ? ExportFailureKind.DiskFull : locked ? ExportFailureKind.FileLocked : ExportFailureKind.InputOutput,
                full ? "No hay espacio suficiente en el disco." : locked ? "El archivo está bloqueado por otra aplicación." : "No se pudo escribir el documento.", exception);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static async Task<ExportRecord?> ReadRecordAsync(string documentPath, CancellationToken cancellationToken = default)
    {
        var path = RecordPath(Path.GetFullPath(documentPath));
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await System.Text.Json.JsonSerializer.DeserializeAsync<ExportRecord>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteRecordAsync(ExportRecord record, CancellationToken token)
    {
        var temporary = RecordPath(record.Path) + ".tmp";
        await using (var stream = File.Create(temporary))
            await System.Text.Json.JsonSerializer.SerializeAsync(stream, record, cancellationToken: token).ConfigureAwait(false);
        File.Move(temporary, RecordPath(record.Path), true);
    }

    private static string RecordPath(string documentPath) => documentPath + ".visualnotes-export.json";

    private static bool CanOpenExclusively(string path)
    {
        try { using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; }
        catch (IOException) { return false; }
    }

    private static async Task EnsureDestinationCanBeReplacedAsync(OpenXmlExportRequest request, string destination, CancellationToken token)
    {
        if (!File.Exists(destination)) return;
        var current = ExportFileVersion.Read(destination);
        var unexpectedlyModified = request.ExpectedExistingVersion is null || request.ExpectedExistingVersion != current;
        var locked = false;
        try { using var _ = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { locked = true; }
        if ((unexpectedlyModified || locked) &&
            (request.ConfirmOverwrite is null || !await request.ConfirmOverwrite(destination, token).ConfigureAwait(false)))
            throw new IOException(locked
                ? "El documento está abierto; se requiere confirmación antes de sobrescribirlo."
                : "El documento cambió desde la última lectura; se requiere confirmación antes de sobrescribirlo.");
    }

    private static void ReplaceAtomically(string temporary, string destination)
    {
        if (File.Exists(destination)) File.Move(temporary, destination, true);
        else File.Move(temporary, destination);
    }

    private static async Task CreatePackageAsync(OpenXmlExportRequest request, string path, CancellationToken token)
    {
        using var package = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = package.AddMainDocumentPart();
        main.Document = new W.Document(new W.Body());
        AddStyles(main);
        var body = main.Document.Body!;
        if (request.Options.Cover)
        {
            body.Append(Paragraph(request.Document.Title, "Title"), Paragraph(DateTimeOffset.Now.ToString("D"), "Subtitle"),
                new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page })));
        }
        if (request.Options.TableOfContents)
        {
            body.Append(Paragraph("Índice", "Heading1"), FieldParagraph("TOC \\o \"1-3\" \\h \\z \\u"));
        }
        uint imageId = 1;
        foreach (var section in request.Document.Sections)
        {
            token.ThrowIfCancellationRequested();
            body.Append(Paragraph(section.Content, "Heading1"));
            foreach (var node in section.Nodes)
                await AppendNodeAsync(main, body, node, request, token, imageId++).ConfigureAwait(false);
        }
        AddPageLayout(main, body, request.Options);
        main.Document.Save();
    }

    private static async Task AppendNodeAsync(MainDocumentPart main, W.Body body, SemanticNode node, OpenXmlExportRequest request, CancellationToken token, uint imageId)
    {
        switch (node.Type)
        {
            case SemanticNodeType.Heading: body.Append(Paragraph(node.Content, "Heading2")); break;
            case SemanticNodeType.Code: body.Append(Paragraph(node.Content, "Code")); break;
            case SemanticNodeType.Context: body.Append(Paragraph(node.Content, "Context")); break;
            case SemanticNodeType.Table: body.Append(CreateTable(node.Content)); break;
            case SemanticNodeType.Image:
                await AppendImageAsync(main, body, node, request, token, imageId).ConfigureAwait(false); break;
            default: body.Append(Paragraph(node.Content, "Normal")); break;
        }
        foreach (var child in node.Nodes)
            await AppendNodeAsync(main, body, child, request, token, ++imageId).ConfigureAwait(false);
    }

    private static async Task AppendImageAsync(MainDocumentPart main, W.Body body, SemanticNode node, OpenXmlExportRequest request, CancellationToken token, uint id)
    {
        var source = node.SourceReferences.FirstOrDefault();
        if (source is null || request.ImageProvider is null) { body.Append(Paragraph(node.Content, "Caption")); return; }
        var supplied = await request.ImageProvider(source.ScreenshotId, token).ConfigureAwait(false);
        if (supplied is null) { body.Append(Paragraph(node.Content, "Caption")); return; }
        var bytes = supplied.Content;
        if (request.Options.ImageMode == ExportImageMode.SourceRegion && source.Region is not null)
            bytes = Crop(bytes, source.Region);
        else if (!string.Equals(supplied.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
            bytes = ConvertToPng(bytes);
        var part = main.AddImagePart(ImagePartType.Png);
        using (var stream = new MemoryStream(bytes)) part.FeedData(stream);
        var relationship = main.GetIdOfPart(part);
        var graphicData = new A.GraphicData(new PIC.Picture(
            new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = id, Name = $"capture-{id}.png" }, new PIC.NonVisualPictureDrawingProperties()),
            new PIC.BlipFill(new A.Blip { Embed = relationship }, new A.Stretch(new A.FillRectangle())),
            new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = ImageWidth, Cy = 3_200_000 }),
                new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle })))
        {
            Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture"
        };

        var drawing = new W.Drawing(new DW.Inline(
            new DW.Extent { Cx = ImageWidth, Cy = 3_200_000 },
            new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new DW.DocProperties { Id = id, Name = $"Captura {id}" },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(graphicData))
        {
            DistanceFromTop = 0,
            DistanceFromBottom = 0,
            DistanceFromLeft = 0,
            DistanceFromRight = 0
        });
        body.Append(new W.Paragraph(new W.Run(drawing)), Paragraph(node.Content, "Caption"));
    }

    private static byte[] Crop(byte[] input, BoundingBox box)
    {
        using var image = Image.Load(input);
        var x = Math.Clamp((int)Math.Round(box.XMin * image.Width), 0, image.Width - 1);
        var y = Math.Clamp((int)Math.Round(box.YMin * image.Height), 0, image.Height - 1);
        var width = Math.Clamp((int)Math.Round((box.XMax - box.XMin) * image.Width), 1, image.Width - x);
        var height = Math.Clamp((int)Math.Round((box.YMax - box.YMin) * image.Height), 1, image.Height - y);
        image.Mutate(context => context.Crop(new Rectangle(x, y, width, height)));
        using var output = new MemoryStream(); image.Save(output, new PngEncoder()); return output.ToArray();
    }

    private static byte[] ConvertToPng(byte[] input)
    {
        using var image = Image.Load(input);
        using var output = new MemoryStream();
        image.Save(output, new PngEncoder());
        return output.ToArray();
    }

    private static W.Paragraph Paragraph(string text, string style) => new(new W.ParagraphProperties(new W.ParagraphStyleId { Val = style }), new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    private static W.Paragraph FieldParagraph(string instruction) => new(new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin }), new W.Run(new W.FieldCode(instruction)), new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.End }));

    private static W.Table CreateTable(string content)
    {
        var table = new W.Table(new W.TableProperties(new W.TableStyle { Val = "TableGrid" }));
        var rows = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .ToArray();
        var columnCount = rows.Length == 0 ? 1 : rows.Max(row => row.Length);
        table.Append(new W.TableGrid(Enumerable.Range(0, columnCount).Select(_ => new W.GridColumn())));
        foreach (var row in rows)
        {
            var cells = row.Concat(Enumerable.Repeat(string.Empty, columnCount - row.Length))
                .Select(cell => new W.TableCell(new W.TableCellProperties(new W.TableCellWidth { Type = W.TableWidthUnitValues.Auto }), Paragraph(cell, "Normal")));
            table.Append(new W.TableRow(cells));
        }
        return table;
    }

    private static void AddStyles(MainDocumentPart main)
    {
        var part = main.AddNewPart<StyleDefinitionsPart>();
        part.Styles = new W.Styles(
            Style("Normal", "Texto", 22), Style("Title", "Título", 44, bold: true), Style("Subtitle", "Subtítulo", 24),
            Style("Heading1", "Título 1", 32, bold: true, outline: 0), Style("Heading2", "Título 2", 28, bold: true, outline: 1),
            Style("Code", "Código", 19, font: "Consolas", shade: "F2F2F2"), Style("Context", "Contexto", 20, italic: true, shade: "EAF2F8"),
            Style("Caption", "Pie", 18, italic: true),
            new W.Style(new W.StyleName { Val = "Tabla con cuadrícula" }) { Type = W.StyleValues.Table, StyleId = "TableGrid" });
        part.Styles.Save();
    }

    private static W.Style Style(string id, string name, int size, bool bold = false, bool italic = false, int? outline = null, string? font = null, string? shade = null)
    {
        var run = new W.StyleRunProperties(new W.RunFonts { Ascii = font ?? "Aptos", HighAnsi = font ?? "Aptos" });
        if (bold) run.Append(new W.Bold { Val = true }); if (italic) run.Append(new W.Italic { Val = true });
        run.Append(new W.FontSize { Val = size.ToString() });
        var paragraph = new W.StyleParagraphProperties(); if (outline.HasValue) paragraph.Append(new W.OutlineLevel { Val = outline.Value });
        if (shade is not null) paragraph.Append(new W.Shading { Val = W.ShadingPatternValues.Clear, Fill = shade });
        return new W.Style(new W.StyleName { Val = name }, paragraph, run) { Type = W.StyleValues.Paragraph, StyleId = id, CustomStyle = id is not ("Normal" or "Title" or "Subtitle" or "Heading1" or "Heading2") };
    }

    private static void AddPageLayout(MainDocumentPart main, W.Body body, OpenXmlExportOptions options)
    {
        var section = new W.SectionProperties();
        if (options.Header)
        {
            var part = main.AddNewPart<HeaderPart>(); part.Header = new W.Header(Paragraph(options.HeaderText, "Normal"));
            section.Append(new W.HeaderReference { Type = W.HeaderFooterValues.Default, Id = main.GetIdOfPart(part) });
        }
        if (options.Footer || options.PageNumbers)
        {
            var part = main.AddNewPart<FooterPart>(); var paragraph = Paragraph(options.Footer ? options.FooterText + " · " : "", "Caption");
            if (options.PageNumbers) paragraph.Append(new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin }), new W.Run(new W.FieldCode("PAGE")), new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.End }));
            part.Footer = new W.Footer(paragraph); section.Append(new W.FooterReference { Type = W.HeaderFooterValues.Default, Id = main.GetIdOfPart(part) });
        }
        section.Append(new W.PageNumberType { Start = 1 }); body.Append(section);
    }
}
