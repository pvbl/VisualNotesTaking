namespace VisualNotes.Infrastructure.LanguageModels;

public class LanguageModelProviderOptions
{
    public required string ApiKey { get; init; }
    public Uri? Endpoint { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);
}

public sealed class OpenAiProviderOptions : LanguageModelProviderOptions;
public sealed class GeminiProviderOptions : LanguageModelProviderOptions;
