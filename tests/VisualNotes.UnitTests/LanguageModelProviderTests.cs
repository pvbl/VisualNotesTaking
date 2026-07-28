using System.Net;
using System.Text;

using Shouldly;

using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.LanguageModels;

using Xunit;

namespace VisualNotes.UnitTests;

[Trait("Category", "Unit")]
public sealed class LanguageModelProviderTests
{
    [Fact]
    public async Task OpenAi_success_is_normalized()
    {
        var provider = OpenAi(Json(HttpStatusCode.OK, """{"model":"gpt-test","choices":[{"message":{"content":"hello"},"finish_reason":"stop"}],"usage":{"prompt_tokens":2,"completion_tokens":1}}"""));
        var response = await provider.GenerateAsync(Request());
        response.ShouldBe(new LanguageModelResponse("hello", "gpt-test", new LanguageModelUsage(2, 1), false, "stop"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{}", LanguageModelErrorKind.Authentication, false)]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limited", LanguageModelErrorKind.RateLimited, true)]
    [InlineData(HttpStatusCode.TooManyRequests, "quota exhausted", LanguageModelErrorKind.BudgetExhausted, false)]
    [InlineData(HttpStatusCode.InternalServerError, "{}", LanguageModelErrorKind.ServiceUnavailable, true)]
    public async Task Http_errors_are_normalized(HttpStatusCode status, string body, LanguageModelErrorKind kind, bool retryable)
    {
        var exception = await Should.ThrowAsync<LanguageModelException>(() => OpenAi(Json(status, body)).GenerateAsync(Request()));
        exception.Kind.ShouldBe(kind);
        exception.Retryable.ShouldBe(retryable);
    }

    [Fact]
    public async Task Invalid_json_is_normalized()
    {
        var exception = await Should.ThrowAsync<LanguageModelException>(() => OpenAi(Json(HttpStatusCode.OK, "not-json")).GenerateAsync(Request()));
        exception.Kind.ShouldBe(LanguageModelErrorKind.InvalidResponse);
    }

    [Fact]
    public async Task Timeout_is_distinguished_from_caller_cancellation()
    {
        var provider = OpenAi(new StubHandler(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Json(HttpStatusCode.OK, "{}"); }));
        var exception = await Should.ThrowAsync<LanguageModelException>(() => provider.GenerateAsync(Request(TimeSpan.FromMilliseconds(10))));
        exception.Kind.ShouldBe(LanguageModelErrorKind.Timeout);
    }

    [Fact]
    public async Task Caller_cancellation_is_normalized()
    {
        var provider = OpenAi(new StubHandler(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Json(HttpStatusCode.OK, "{}"); }));
        using var source = new CancellationTokenSource();
        source.Cancel();
        var exception = await Should.ThrowAsync<LanguageModelException>(() => provider.GenerateAsync(Request(), source.Token));
        exception.Kind.ShouldBe(LanguageModelErrorKind.Cancelled);
    }

    [Fact]
    public async Task Truncated_output_is_returned_as_partial()
    {
        var provider = OpenAi(Json(HttpStatusCode.OK, """{"model":"gpt-test","choices":[{"message":{"content":"partial"},"finish_reason":"length"}]}"""));
        var response = await provider.GenerateAsync(Request());
        response.IsPartial.ShouldBeTrue();
        response.Text.ShouldBe("partial");
    }

    [Fact]
    public async Task Providers_satisfy_the_same_response_contract()
    {
        ITextLanguageModelProvider openAi = OpenAi(Json(HttpStatusCode.OK, """{"model":"shared-model","choices":[{"message":{"content":"same"},"finish_reason":"stop"}],"usage":{"prompt_tokens":4,"completion_tokens":2}}"""));
        ITextLanguageModelProvider gemini = Gemini(Json(HttpStatusCode.OK, """{"modelVersion":"shared-model","candidates":[{"content":{"parts":[{"text":"same"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":4,"candidatesTokenCount":2}}"""));

        var expected = new LanguageModelResponse("same", "shared-model", new LanguageModelUsage(4, 2), false, "stop");
        Normalize(await openAi.GenerateAsync(Request())).ShouldBe(expected);
        Normalize(await gemini.GenerateAsync(Request())).ShouldBe(expected);
    }

    private static LanguageModelResponse Normalize(LanguageModelResponse response) => response with { FinishReason = response.FinishReason?.ToLowerInvariant() };
    private static TextLanguageModelRequest Request(TimeSpan? timeout = null) => new("prompt", new LanguageModelOptions("model", Timeout: timeout));
    private static OpenAiLanguageModelProvider OpenAi(HttpMessageHandler handler) => new(new HttpClient(handler), new OpenAiProviderOptions { ApiKey = "test" });
    private static OpenAiLanguageModelProvider OpenAi(HttpResponseMessage response) => OpenAi(new StubHandler((_, _) => Task.FromResult(response)));
    private static GeminiLanguageModelProvider Gemini(HttpResponseMessage response) => new(new HttpClient(new StubHandler((_, _) => Task.FromResult(response))), new GeminiProviderOptions { ApiKey = "test" });
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request, cancellationToken);
    }
}
