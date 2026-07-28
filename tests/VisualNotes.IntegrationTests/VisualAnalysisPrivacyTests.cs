using Microsoft.EntityFrameworkCore;

using Shouldly;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.Persistence;
using VisualNotes.Infrastructure.Processing;

namespace VisualNotes.IntegrationTests;

public sealed class VisualAnalysisPrivacyTests : IAsyncDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "visualnotes-privacy-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Image_never_reaches_provider_without_consent_and_credential(bool hasConsent, bool hasCredential)
    {
        var (factory, job) = await ArrangeAsync();
        var provider = new DeterministicVlmProvider();
        var handler = new VisualAnalysisJobHandler(factory, root, new CredentialStore(hasCredential),
            new Consent(hasConsent), new FileExtractionArtifactStore(root), (_, _) => provider);

        await Should.ThrowAsync<LanguageModelException>(() => handler.ExecuteAsync(job, default));

        provider.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Deterministic_provider_completes_the_persisted_end_to_end_job()
    {
        var (factory, job) = await ArrangeAsync();
        var provider = new DeterministicVlmProvider();
        var handler = new VisualAnalysisJobHandler(factory, root, new CredentialStore(true), new Consent(true),
            new FileExtractionArtifactStore(root), (_, _) => provider);
        await using var processor = new DurableAnalysisJobProcessor(factory, handler, new(1, 1));

        await processor.EnqueueAsync(job);
        (await processor.RunManualAsync()).ShouldBe(1);

        await using var db = await factory.CreateDbContextAsync();
        var persisted = await db.AnalysisJobs.Include(x => x.Result).SingleAsync();
        persisted.JobStatus.ShouldBe(AnalysisJobStatus.Completed);
        persisted.Result!.ExtractedText.ShouldContain("Deterministic");
        provider.RequestCount.ShouldBe(1);
        (await db.Screenshots.SingleAsync()).ProcessingStatus.ShouldBe(ScreenshotStatus.Ready);
    }

    [Fact]
    public async Task Batch_prompt_contains_capture_context_and_markdown_notes_without_images()
    {
        var (factory, job) = await ArrangeAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var screenshot = await db.Screenshots.SingleAsync();
            screenshot.UserContext = "**Contexto de la diapositiva**";
            db.Screenshots.Add(new Screenshot
            {
                SessionId = screenshot.SessionId,
                CapturedAt = screenshot.CapturedAt.AddSeconds(1),
                UserContext = "## Mi apunte\nRelacionar con el tema anterior.",
                ProcessingStatus = ScreenshotStatus.Ready
            });
            db.AnalysisJobs.Add(job);
            await db.SaveChangesAsync();
        }
        job.Trigger = AnalysisJobTrigger.Batch;
        var provider = new DeterministicVlmProvider();
        var handler = new VisualAnalysisJobHandler(factory, root, new CredentialStore(true), new Consent(true),
            new FileExtractionArtifactStore(root), (_, _) => provider);

        await handler.ExecuteAsync(job, default);

        provider.LastPrompt.ShouldContain("## Contexto Markdown de la sesión");
        provider.LastPrompt.ShouldContain("## Información de la sesión");
        provider.LastPrompt.ShouldContain("**Contexto de la diapositiva**");
        provider.LastPrompt.ShouldContain("## Mi apunte");
        provider.LastPrompt.ShouldContain("Apunte sin captura");
    }

    private async Task<(Factory Factory, AnalysisJob Job)> ArrangeAsync()
    {
        Directory.CreateDirectory(root);
        var factory = new Factory(Path.Combine(root, "jobs.db"));
        await using var db = await factory.CreateDbContextAsync();
        await new DatabaseMigrationService(db).MigrateAsync();
        var session = new NoteSession();
        var screenshot = new Screenshot { Session = session, Width = 1, Height = 1 };
        var relative = $"sessions/{session.Id:N}/originals/{screenshot.Id:N}.png";
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var image = new Image<Rgba32>(1, 1)) await image.SaveAsPngAsync(path);
        screenshot.Image = new ScreenshotImage { RelativePath = relative, MediaType = "image/png" };
        db.Screenshots.Add(screenshot);
        await db.SaveChangesAsync();
        return (factory, new AnalysisJob { ScreenshotId = screenshot.Id, Trigger = AnalysisJobTrigger.Manual, IdempotencyKey = Guid.NewGuid().ToString("N") });
    }

    public ValueTask DisposeAsync() { if (Directory.Exists(root)) Directory.Delete(root, true); return ValueTask.CompletedTask; }

    private sealed class Consent(bool value) : IImageTransmissionConsent { public Task<bool> HasConsentAsync(Guid screenshotId, CancellationToken cancellationToken = default) => Task.FromResult(value); }
    private sealed class CredentialStore(bool exists) : IApiCredentialStore
    {
        public Task<string?> GetAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default) => Task.FromResult<string?>(exists ? "secret" : null);
        public Task SaveAsync(ApiCredentialProfile profile, string credential, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(exists);
        public Task<bool> VerifyAsync(ApiCredentialProfile profile, string candidate, CancellationToken cancellationToken = default) => Task.FromResult(exists);
        public Task DeleteAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetMaskedAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
    private sealed class DeterministicVlmProvider : IVisionLanguageModelProvider
    {
        public int RequestCount { get; private set; }
        public string LastPrompt { get; private set; } = string.Empty;
        public Task<LanguageModelResponse> GenerateAsync(VisionLanguageModelRequest request, CancellationToken cancellationToken = default)
        {
            RequestCount++;
            LastPrompt = request.Prompt;
            const string json = """{"language":"en","contentType":"slide","title":"Deterministic","summary":"Stable result","transcription":"Deterministic transcription","code":[],"equations":[],"tables":[],"coordinateSystem":"Normalized1000","regions":[],"concepts":[],"confidence":1,"warnings":[]}""";
            return Task.FromResult(new LanguageModelResponse(json, request.Options.Model, new(null, null)));
        }
    }
    private sealed class Factory(string path) : IDbContextFactory<VisualNotesDbContext>, IAsyncDisposable
    {
        public VisualNotesDbContext CreateDbContext() => new(new DbContextOptionsBuilder<VisualNotesDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
        public Task<VisualNotesDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
