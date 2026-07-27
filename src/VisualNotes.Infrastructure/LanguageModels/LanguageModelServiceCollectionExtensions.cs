using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using VisualNotes.Core.Services;

namespace VisualNotes.Infrastructure.LanguageModels;

public static class LanguageModelServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAiLanguageModelProvider(this IServiceCollection services, OpenAiProviderOptions options)
    {
        services.AddSingleton(options);
        services.AddHttpClient<OpenAiLanguageModelProvider>().AddProviderResilience();
        services.AddTransient<IVisionLanguageModelProvider>(provider => provider.GetRequiredService<OpenAiLanguageModelProvider>());
        services.AddTransient<ITextLanguageModelProvider>(provider => provider.GetRequiredService<OpenAiLanguageModelProvider>());
        return services;
    }

    public static IServiceCollection AddGeminiLanguageModelProvider(this IServiceCollection services, GeminiProviderOptions options)
    {
        services.AddSingleton(options);
        services.AddHttpClient<GeminiLanguageModelProvider>().AddProviderResilience();
        services.AddTransient<IVisionLanguageModelProvider>(provider => provider.GetRequiredService<GeminiLanguageModelProvider>());
        services.AddTransient<ITextLanguageModelProvider>(provider => provider.GetRequiredService<GeminiLanguageModelProvider>());
        return services;
    }

    private static IHttpClientBuilder AddProviderResilience(this IHttpClientBuilder builder) =>
        builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 2;
            options.Retry.Delay = TimeSpan.FromMilliseconds(200);
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;
            options.Retry.ShouldHandle = static arguments => ValueTask.FromResult(
                arguments.Outcome.Exception is HttpRequestException ||
                arguments.Outcome.Result?.StatusCode is System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests ||
                (int?)arguments.Outcome.Result?.StatusCode >= 500);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
            options.CircuitBreaker.MinimumThroughput = 5;
            options.CircuitBreaker.ShouldHandle = static arguments => ValueTask.FromResult(
                arguments.Outcome.Exception is HttpRequestException || (int?)arguments.Outcome.Result?.StatusCode >= 500);
        });
}
