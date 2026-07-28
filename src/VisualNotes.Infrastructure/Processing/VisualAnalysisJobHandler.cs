using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.LanguageModels;
using VisualNotes.Infrastructure.Persistence;

namespace VisualNotes.Infrastructure.Processing;

public interface IImageTransmissionConsent
{
    Task<bool> HasConsentAsync(Guid screenshotId, CancellationToken cancellationToken = default);
}

public sealed class SettingsImageTransmissionConsent(ISettingsRepository settings) : IImageTransmissionConsent
{
    public const string Key = "privacy.image-upload-consent";
    public async Task<bool> HasConsentAsync(Guid screenshotId, CancellationToken cancellationToken = default) =>
        await settings.GetAsync<bool?>(Key, cancellationToken) == true;
}

public sealed class ImageSharpVisualSourceNormalizer : IVisualSourceNormalizer
{
    public async Task<NormalizedVisualSource> NormalizeAsync(VisualSource source, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(source.Content.ToArray(), writable: false);
        using var image = await Image.LoadAsync<Rgba32>(stream, cancellationToken);
        var pixels = new byte[checked(image.Width * image.Height * 4)];
        image.CopyPixelDataTo(pixels);
        return new(source.Name, "image/png", pixels, image.Width, image.Height);
    }
}

/// <summary>Resolves all remote inputs before reading image bytes; this ordering is a privacy boundary.</summary>
public sealed class VisualAnalysisJobHandler(
    IDbContextFactory<VisualNotesDbContext> contexts,
    string dataDirectory,
    IApiCredentialStore credentials,
    IImageTransmissionConsent consent,
    IExtractionArtifactStore artifacts,
    Func<string, string, IVisionLanguageModelProvider>? providerFactory = null) : IAnalysisJobHandler
{
    public async Task<CaptureAnalysis> ExecuteAsync(AnalysisJob job, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var screenshot = await db.Screenshots.Include(x => x.Image).SingleOrDefaultAsync(x => x.Id == job.ScreenshotId, cancellationToken)
            ?? throw Configuration("No existe la captura del trabajo.");
        var document = await new SettingsRepository(db).GetAsync<SettingsDocument>("hierarchical-settings", cancellationToken) ?? new();
        var defaults = new SettingsValues { Language = "Español", Provider = "OpenAI", Model = "gpt-4.1-mini", PromptTemplate = VisualExtractionPipeline.ExtractionPrompt, IncludeImages = true, MaximumImageSide = 2560 };
        var effective = new EffectiveSettingsResolver().Resolve(new(defaults, document.Global,
            document.Sessions.GetValueOrDefault(screenshot.SessionId),
            screenshot.SectionId is { } section ? document.Sections.GetValueOrDefault(section) : null,
            document.ScreenshotOverrides.GetValueOrDefault(screenshot.Id)));
        var profile = job.ProviderProfileId is { } profileId
            ? await db.ProviderProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == profileId, cancellationToken)
            : null;
        var providerName = profile?.Provider ?? effective.Provider.Value;
        var model = profile?.Model ?? effective.Model.Value;
        if (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(model)) throw Configuration("Proveedor o modelo no configurado.");
        if (!effective.IncludeImages.Value || !await consent.HasConsentAsync(screenshot.Id, cancellationToken))
            throw Configuration("Se requiere consentimiento explícito para enviar la imagen.");
        var apiKey = await credentials.GetAsync(ApiCredentialProfile.Extraction, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey)) throw Configuration("Falta la credencial efectiva de extracción.");
        if (screenshot.Image is null || string.IsNullOrWhiteSpace(screenshot.Image.RelativePath)) throw Configuration("La captura no tiene un artefacto de imagen.");

        var endpoint = Uri.TryCreate(profile?.Endpoint, UriKind.Absolute, out var parsedEndpoint) ? parsedEndpoint : null;
        var provider = providerFactory?.Invoke(providerName, apiKey) ?? CreateProvider(providerName, apiKey, endpoint);
        var imagePath = StoragePath.Resolve(dataDirectory, screenshot.Image.RelativePath);
        var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var persistedJob = await db.AnalysisJobs.SingleAsync(x => x.Id == job.Id, cancellationToken);
        persistedJob.EffectivePromptSnapshotJson = JsonSerializer.Serialize(new { Provider = providerName, Model = model, Template = effective.PromptTemplate.Value });
        await db.SaveChangesAsync(cancellationToken);
        var result = await new VisualExtractionPipeline(new ImageSharpVisualSourceNormalizer(), provider, artifacts,
                extractionPrompt: effective.PromptTemplate.Value)
            .ExtractAsync(new(Path.GetFileName(imagePath), screenshot.Image.MediaType, bytes, screenshot.Width, screenshot.Height),
                new(model, endpoint), cancellationToken);
        return new CaptureAnalysis { ExtractedText = result.Note, Summary = result.Response.Summary, RawResultRelativePath = result.Extraction.ExtractionId };
    }

    private static IVisionLanguageModelProvider CreateProvider(string name, string key, Uri? endpoint) => name.ToLowerInvariant() switch
    {
        "openai" => new OpenAiLanguageModelProvider(new HttpClient(), new() { ApiKey = key, Endpoint = endpoint }),
        "gemini" => new GeminiLanguageModelProvider(new HttpClient(), new() { ApiKey = key, Endpoint = endpoint }),
        _ => throw Configuration($"Proveedor no registrado: {name}.")
    };

    private static LanguageModelException Configuration(string message) =>
        new(LanguageModelErrorKind.InvalidRequest, message, retryable: false);
}
