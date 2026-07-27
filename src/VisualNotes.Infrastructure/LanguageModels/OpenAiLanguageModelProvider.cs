using System.Net.Http.Headers;
using System.Text.Json;
using VisualNotes.Core.Services;

namespace VisualNotes.Infrastructure.LanguageModels;

public sealed class OpenAiLanguageModelProvider : LanguageModelHttpProvider, IVisionLanguageModelProvider, ITextLanguageModelProvider
{
    private static readonly Uri DefaultEndpoint = new("https://api.openai.com/v1/chat/completions");
    private readonly LanguageModelProviderOptions providerOptions;

    public OpenAiLanguageModelProvider(HttpClient client, OpenAiProviderOptions providerOptions) : base(client) => this.providerOptions = providerOptions;

    public Task<LanguageModelResponse> GenerateAsync(TextLanguageModelRequest request, CancellationToken cancellationToken = default) =>
        GenerateAsync(request.Options, new object[] { new { type = "text", text = request.Prompt } }, cancellationToken);

    public Task<LanguageModelResponse> GenerateAsync(VisionLanguageModelRequest request, CancellationToken cancellationToken = default) =>
        GenerateAsync(request.Options, new object[]
        {
            new { type = "text", text = request.Prompt },
            new { type = "image_url", image_url = new { url = $"data:{request.MediaType};base64,{Convert.ToBase64String(request.Image.Span)}", detail = request.ImageDetail.ToString().ToLowerInvariant() } }
        }, cancellationToken);

    private async Task<LanguageModelResponse> GenerateAsync(LanguageModelOptions options, object[] content, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
            ["messages"] = new[] { new { role = "user", content } }
        };
        if (options.MaximumOutputTokens is not null) body["max_tokens"] = options.MaximumOutputTokens;
        AddParameters(body, options.Parameters);

        var message = new HttpRequestMessage(HttpMethod.Post, options.Endpoint ?? providerOptions.Endpoint ?? DefaultEndpoint) { Content = Json(body) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", providerOptions.ApiKey);
        using var json = await SendAsync(message, options.Timeout ?? providerOptions.Timeout, cancellationToken).ConfigureAwait(false);
        try
        {
            var root = json.RootElement;
            var choice = root.GetProperty("choices")[0];
            var reason = choice.TryGetProperty("finish_reason", out var finish) ? finish.GetString() : null;
            var usage = root.TryGetProperty("usage", out var usageElement) ? usageElement : default;
            return new LanguageModelResponse(
                choice.GetProperty("message").GetProperty("content").GetString() ?? string.Empty,
                root.TryGetProperty("model", out var model) ? model.GetString() ?? options.Model : options.Model,
                new LanguageModelUsage(GetInt(usage, "prompt_tokens"), GetInt(usage, "completion_tokens")),
                reason is "length" or "content_filter", reason);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new LanguageModelException(LanguageModelErrorKind.InvalidResponse, "The OpenAI response was incomplete.", innerException: exception);
        }
    }

    private static int? GetInt(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value.GetInt32() : null;
    private static void AddParameters(Dictionary<string, object?> body, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null) return;
        foreach (var parameter in parameters) body[parameter.Key] = parameter.Value;
    }
}
