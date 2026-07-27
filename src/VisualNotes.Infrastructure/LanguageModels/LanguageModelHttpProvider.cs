using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VisualNotes.Core.Services;

namespace VisualNotes.Infrastructure.LanguageModels;

public abstract class LanguageModelHttpProvider
{
    private readonly HttpClient client;

    protected LanguageModelHttpProvider(HttpClient client) => this.client = client;

    protected async Task<JsonDocument> SendAsync(HttpRequestMessage message, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using (message)
        using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutSource.CancelAfter(timeout);
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new LanguageModelException(LanguageModelErrorKind.Timeout, "The language model request timed out.", retryable: true, innerException: exception);
            }
            catch (OperationCanceledException exception)
            {
                throw new LanguageModelException(LanguageModelErrorKind.Cancelled, "The language model request was cancelled.", innerException: exception);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateHttpExceptionAsync(response, cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (JsonException exception)
                {
                    throw new LanguageModelException(LanguageModelErrorKind.InvalidResponse, "The provider returned invalid JSON.", (int)response.StatusCode, innerException: exception);
                }
            }
        }
    }

    private static async Task<LanguageModelException> CreateHttpExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => LanguageModelErrorKind.Authentication,
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity => LanguageModelErrorKind.InvalidRequest,
            HttpStatusCode.TooManyRequests when body.Contains("quota", StringComparison.OrdinalIgnoreCase) || body.Contains("billing", StringComparison.OrdinalIgnoreCase) => LanguageModelErrorKind.BudgetExhausted,
            HttpStatusCode.TooManyRequests => LanguageModelErrorKind.RateLimited,
            >= HttpStatusCode.InternalServerError => LanguageModelErrorKind.ServiceUnavailable,
            _ => LanguageModelErrorKind.Unknown
        };
        var retryable = kind is LanguageModelErrorKind.RateLimited or LanguageModelErrorKind.ServiceUnavailable;
        return new LanguageModelException(kind, $"Language model provider returned HTTP {status}.", status, retryable);
    }

    protected static JsonContent Json(object value) => JsonContent.Create(value, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
