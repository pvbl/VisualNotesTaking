using System.Text.Json;

using VisualNotes.Core.Services;

namespace VisualNotes.Infrastructure.LanguageModels;

public sealed class GeminiLanguageModelProvider : LanguageModelHttpProvider, IVisionLanguageModelProvider, ITextLanguageModelProvider
{
    private readonly LanguageModelProviderOptions providerOptions;

    public GeminiLanguageModelProvider(HttpClient client, GeminiProviderOptions providerOptions) : base(client) => this.providerOptions = providerOptions;

    public Task<LanguageModelResponse> GenerateAsync(TextLanguageModelRequest request, CancellationToken cancellationToken = default) =>
        GenerateAsync(request.Options, new object[] { new { text = request.Prompt } }, cancellationToken);

    public Task<LanguageModelResponse> GenerateAsync(VisionLanguageModelRequest request, CancellationToken cancellationToken = default) =>
        GenerateAsync(request.Options, new object[] { new { text = request.Prompt }, new { inline_data = new { mime_type = request.MediaType, data = Convert.ToBase64String(request.Image.Span) } } }, cancellationToken);

    private async Task<LanguageModelResponse> GenerateAsync(LanguageModelOptions options, object[] parts, CancellationToken cancellationToken)
    {
        var generation = new Dictionary<string, object?>();
        if (options.MaximumOutputTokens is not null) generation["maxOutputTokens"] = options.MaximumOutputTokens;
        if (options.Parameters is not null) foreach (var parameter in options.Parameters) generation[parameter.Key] = parameter.Value;
        var body = new Dictionary<string, object?> { ["contents"] = new[] { new { role = "user", parts } } };
        if (generation.Count != 0) body["generationConfig"] = generation;
        var endpoint = options.Endpoint ?? providerOptions.Endpoint ?? new Uri($"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(options.Model)}:generateContent");
        var separator = endpoint.Query.Length == 0 ? "?" : "&";
        var message = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint + separator + "key=" + Uri.EscapeDataString(providerOptions.ApiKey))) { Content = Json(body) };
        using var json = await SendAsync(message, options.Timeout ?? providerOptions.Timeout, cancellationToken).ConfigureAwait(false);
        try
        {
            var root = json.RootElement;
            var candidate = root.GetProperty("candidates")[0];
            var reason = candidate.TryGetProperty("finishReason", out var finish) ? finish.GetString() : null;
            var usage = root.TryGetProperty("usageMetadata", out var usageElement) ? usageElement : default;
            return new LanguageModelResponse(
                candidate.GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? string.Empty,
                root.TryGetProperty("modelVersion", out var model) ? model.GetString() ?? options.Model : options.Model,
                new LanguageModelUsage(GetInt(usage, "promptTokenCount"), GetInt(usage, "candidatesTokenCount")),
                reason is "MAX_TOKENS" or "SAFETY", reason);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new LanguageModelException(LanguageModelErrorKind.InvalidResponse, "The Gemini response was incomplete.", innerException: exception);
        }
    }

    private static int? GetInt(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value.GetInt32() : null;
}
