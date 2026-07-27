namespace VisualNotes.Core.Services;

public interface IVisionLanguageModelProvider
{
    Task<LanguageModelResponse> GenerateAsync(VisionLanguageModelRequest request, CancellationToken cancellationToken = default);
}

public interface ITextLanguageModelProvider
{
    Task<LanguageModelResponse> GenerateAsync(TextLanguageModelRequest request, CancellationToken cancellationToken = default);
}

public enum ImageDetail { Auto, Low, High }

public sealed record LanguageModelOptions(
    string Model,
    Uri? Endpoint = null,
    TimeSpan? Timeout = null,
    int? MaximumOutputTokens = null,
    IReadOnlyDictionary<string, object?>? Parameters = null);

public sealed record TextLanguageModelRequest(string Prompt, LanguageModelOptions Options);

public sealed record VisionLanguageModelRequest(
    string Prompt,
    ReadOnlyMemory<byte> Image,
    string MediaType,
    LanguageModelOptions Options,
    ImageDetail ImageDetail = ImageDetail.Auto);

public sealed record LanguageModelUsage(int? InputTokens, int? OutputTokens);

public sealed record LanguageModelResponse(
    string Text,
    string Model,
    LanguageModelUsage Usage,
    bool IsPartial = false,
    string? FinishReason = null);

public enum LanguageModelErrorKind
{
    Authentication,
    InvalidRequest,
    BudgetExhausted,
    RateLimited,
    Timeout,
    Cancelled,
    ServiceUnavailable,
    InvalidResponse,
    Unknown
}

public sealed class LanguageModelException : Exception
{
    public LanguageModelException(LanguageModelErrorKind kind, string message, int? statusCode = null, bool retryable = false, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
        Retryable = retryable;
    }

    public LanguageModelErrorKind Kind { get; }
    public int? StatusCode { get; }
    public bool Retryable { get; }
}
