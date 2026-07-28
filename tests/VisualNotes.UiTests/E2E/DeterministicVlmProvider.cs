using System.Security.Cryptography;
using System.Text;
using VisualNotes.Core.Services;

namespace VisualNotes.UiTests.E2E;

/// <summary>A network-free VLM double whose output is stable for identical input.</summary>
internal sealed class DeterministicVlmProvider : IVisionLanguageModelProvider
{
    public Task<LanguageModelResponse> GenerateAsync(
        VisionLanguageModelRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var promptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Prompt)))[..12];
        var imageHash = Convert.ToHexString(SHA256.HashData(request.Image.Span))[..12];
        var response = $$"""
            {"schemaVersion":1,"title":"Análisis simulado","summary":"Resultado determinista","promptHash":"{{promptHash}}","imageHash":"{{imageHash}}","needsReview":true}
            """;
        return Task.FromResult(new LanguageModelResponse(
            response, "fake-vlm-e2e-v1", new LanguageModelUsage(17, 23), FinishReason: "stop"));
    }
}
