using System.Net;
using System.Text;
using Shouldly;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.LanguageModels;
using Xunit;

namespace VisualNotes.UnitTests;

public abstract class LanguageModelProviderContractTests
{
    protected abstract ITextLanguageModelProvider Create(HttpMessageHandler handler);
    protected abstract string SuccessPayload { get; }

    [Fact, Trait("Category", "Unit")]
    public async Task Successful_response_obeys_the_normalized_contract()
    {
        var response = await Create(Respond(HttpStatusCode.OK, SuccessPayload)).GenerateAsync(Request());
        response.Text.ShouldBe("contract text");
        response.Model.ShouldBe("contract-model");
        response.Usage.ShouldBe(new LanguageModelUsage(7, 3));
        response.IsPartial.ShouldBeFalse();
    }

    [Fact, Trait("Category", "Unit")]
    public async Task Invalid_payload_uses_the_shared_error_taxonomy()
    {
        var error = await Should.ThrowAsync<LanguageModelException>(() =>
            Create(Respond(HttpStatusCode.OK, "not-json")).GenerateAsync(Request()));
        error.Kind.ShouldBe(LanguageModelErrorKind.InvalidResponse);
        error.Retryable.ShouldBeFalse();
    }

    [Fact, Trait("Category", "Unit")]
    public async Task Caller_cancellation_reaches_the_transport_and_is_normalized()
    {
        var transportObservedCancellation = false;
        var provider = Create(new StubHandler(async (_, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { transportObservedCancellation = true; throw; }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Should.ThrowAsync<LanguageModelException>(() => provider.GenerateAsync(Request(), cancellation.Token));
        error.Kind.ShouldBe(LanguageModelErrorKind.Cancelled);
        transportObservedCancellation.ShouldBeTrue();
    }

    private static TextLanguageModelRequest Request() => new("contract prompt", new("contract-model"));
    private static StubHandler Respond(HttpStatusCode status, string body) => new((_, _) => Task.FromResult(
        new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));

    protected sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request, cancellationToken);
    }
}

public sealed class OpenAiLanguageModelProviderContractTests : LanguageModelProviderContractTests
{
    protected override string SuccessPayload => """{"model":"contract-model","choices":[{"message":{"content":"contract text"},"finish_reason":"stop"}],"usage":{"prompt_tokens":7,"completion_tokens":3}}""";
    protected override ITextLanguageModelProvider Create(HttpMessageHandler handler) =>
        new OpenAiLanguageModelProvider(new HttpClient(handler), new OpenAiProviderOptions { ApiKey = "contract" });
}

public sealed class GeminiLanguageModelProviderContractTests : LanguageModelProviderContractTests
{
    protected override string SuccessPayload => """{"modelVersion":"contract-model","candidates":[{"content":{"parts":[{"text":"contract text"}]} ,"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":7,"candidatesTokenCount":3}}""";
    protected override ITextLanguageModelProvider Create(HttpMessageHandler handler) =>
        new GeminiLanguageModelProvider(new HttpClient(handler), new GeminiProviderOptions { ApiKey = "contract" });
}
