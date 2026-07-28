using DocumentFormat.OpenXml.Packaging;

using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure;

namespace VisualNotes.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class PersistedCaptureDocumentJourneyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VisualNotes-journey-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Persisted_capture_can_be_reviewed_composed_and_exported_to_docx()
    {
        await using var runtime = await VisualNotesRuntime.CreateAsync(_root, CancellationToken.None);
        var session = await runtime.Coordinator.CreateAsync(new NoteSession { Name = "Álgebra lineal" });
        var section = await runtime.Coordinator.AddSectionAsync(session, "Matrices");
        var job = new AnalysisJob
        {
            JobStatus = AnalysisJobStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = "journey-analysis",
            Result = new CaptureAnalysis { ExtractedText = "Una matriz identidad conserva todos los vectores." }
        };
        var capture = new Screenshot
        {
            SessionId = session.Id,
            SectionId = section.Id,
            ProcessingStatus = ScreenshotStatus.NeedsReview,
            UserContext = "Definición destacada por el estudiante",
            AnalysisJobs = [job]
        };
        job.ScreenshotId = capture.Id;
        await runtime.Coordinator.AddCaptureAsync(session, capture);

        var review = await runtime.CaptureWorkspace.LoadAsync(session.Id);
        review.Single().ProcessingStatus = ScreenshotStatus.Ready;
        review.Single().Tags = "examen";
        await runtime.CaptureWorkspace.SaveAsync(review);

        var document = await runtime.CaptureWorkspace.ComposeAsync(session);
        document.Sections.Single().Content.ShouldBe("Matrices");
        document.Sections.Single().Nodes.Select(x => x.Content).ShouldContain("Una matriz identidad conserva todos los vectores.");
        var path = Path.Combine(_root, "apuntes.docx");
        await runtime.DocumentExporter.ExportAsync(document, path);

        using var package = WordprocessingDocument.Open(path, false);
        var text = package.MainDocumentPart!.Document.Body!.InnerText;
        text.ShouldContain("Álgebra lineal");
        text.ShouldContain("Matrices");
        text.ShouldContain("Definición destacada por el estudiante");
        text.ShouldContain("Una matriz identidad conserva todos los vectores.");
        (await runtime.CaptureWorkspace.LoadAsync(session.Id)).Single().Tags.ShouldBe("examen");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
