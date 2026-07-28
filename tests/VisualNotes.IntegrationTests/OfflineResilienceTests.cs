using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.LanguageModels;
using VisualNotes.Infrastructure.Persistence;

namespace VisualNotes.IntegrationTests;

/// <summary>
/// Deterministic fault matrix for boundaries that receive untrusted data. All doubles run in-process;
/// this suite deliberately has no network, cloud, or platform-service dependency.
/// </summary>
[Trait("Category", "Integration")]
public sealed class OfflineResilienceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "visualnotes-resilience-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"language\":42}")]
    [InlineData("{\"language\":{\"unexpected\":true}}")]
    public void Truncated_or_wrongly_typed_vlm_json_has_an_explicit_validation_error(string json) =>
        Should.Throw<AnalysisResponseValidationException>(() => StructuredAnalysisResponseParser.Parse(json));

    [Fact]
    public void Excessively_deep_vlm_json_is_rejected_at_the_parser_boundary()
    {
        var json = new string('[', 40) + new string(']', 40);
        Should.Throw<AnalysisResponseValidationException>(() => StructuredAnalysisResponseParser.Parse(json))
            .InnerException.ShouldBeOfType<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task Empty_corrupt_oversized_and_excessive_pixel_images_are_rejected_without_artifacts()
    {
        var service = new ScreenshotStorageService(root, reservedFreeBytes: 0, maximumInputBytes: 64, maximumPixels: 100);
        await Should.ThrowAsync<UnknownImageFormatException>(() => Store(service, []));
        await Should.ThrowAsync<UnknownImageFormatException>(() => Store(service, [0x89, 0x50, 0x4e, 0x47, 0, 1, 2]));
        await Should.ThrowAsync<InvalidDataException>(() => Store(service, new byte[65]));

        var image = CreatePng(11, 10);
        await Should.ThrowAsync<InvalidDataException>(() => Store(new ScreenshotStorageService(root, 0, image.Length, 100), image));
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, LanguageModelErrorKind.RateLimited, true)]
    [InlineData(HttpStatusCode.InternalServerError, LanguageModelErrorKind.ServiceUnavailable, true)]
    public async Task Http_failures_are_explicit_and_recoverable(HttpStatusCode status, LanguageModelErrorKind kind, bool retryable)
    {
        var error = await Should.ThrowAsync<LanguageModelException>(() => Provider(new ResponseHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("failure") }))).GenerateAsync(Request()));
        error.Kind.ShouldBe(kind);
        error.Retryable.ShouldBe(retryable);
    }

    [Fact]
    public async Task Disconnect_and_partial_response_are_distinguished()
    {
        var disconnected = Provider(new ResponseHandler((_, _) => throw new HttpRequestException("connection reset")));
        var error = await Should.ThrowAsync<LanguageModelException>(() => disconnected.GenerateAsync(Request()));
        error.Kind.ShouldBe(LanguageModelErrorKind.ServiceUnavailable);
        error.Retryable.ShouldBeTrue();

        var partial = Provider(new ResponseHandler((_, _) => Task.FromResult(Json("""{"model":"offline","choices":[{"message":{"content":"usable prefix"},"finish_reason":"length"}]}"""))));
        (await partial.GenerateAsync(Request())).IsPartial.ShouldBeTrue();
    }

    [Fact]
    public async Task Timeout_and_caller_cancellation_are_distinguished_without_external_services()
    {
        var provider = Provider(new ResponseHandler(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Json("{}"); }));
        (await Should.ThrowAsync<LanguageModelException>(() => provider.GenerateAsync(Request(TimeSpan.FromMilliseconds(10))))).Kind.ShouldBe(LanguageModelErrorKind.Timeout);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        (await Should.ThrowAsync<LanguageModelException>(() => provider.GenerateAsync(Request(), cancelled.Token))).Kind.ShouldBe(LanguageModelErrorKind.Cancelled);
    }

    [Fact]
    public async Task Oversized_and_deep_http_json_are_bounded()
    {
        var oversized = Json(new string(' ', LanguageModelHttpProvider.MaximumResponseBytes + 1));
        (await Should.ThrowAsync<LanguageModelException>(() => Provider(new ResponseHandler((_, _) => Task.FromResult(oversized))).GenerateAsync(Request()))).Kind
            .ShouldBe(LanguageModelErrorKind.InvalidResponse);

        var deep = Json(new string('[', 40) + new string(']', 40));
        (await Should.ThrowAsync<LanguageModelException>(() => Provider(new ResponseHandler((_, _) => Task.FromResult(deep))).GenerateAsync(Request()))).Kind
            .ShouldBe(LanguageModelErrorKind.InvalidResponse);
    }

    [Fact]
    public async Task Sqlite_busy_is_explicit_and_the_committed_state_remains_valid()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "busy.db");
        var options = new DbContextOptionsBuilder<VisualNotesDbContext>().UseSqlite($"Data Source={path};Default Timeout=0;Pooling=False").Options;
        await using (var setup = new VisualNotesDbContext(options)) { await setup.Database.EnsureCreatedAsync(); }
        await using var lockConnection = new SqliteConnection($"Data Source={path};Default Timeout=0;Pooling=False");
        await lockConnection.OpenAsync();
        await using var command = lockConnection.CreateCommand();
        command.CommandText = "BEGIN EXCLUSIVE;";
        await command.ExecuteNonQueryAsync();
        await using var blocked = new VisualNotesDbContext(options);
        blocked.Sessions.Add(new NoteSession { Name = "must-not-commit" });
        await Should.ThrowAsync<SqliteException>(() => blocked.SaveChangesAsync());
        command.CommandText = "ROLLBACK;";
        await command.ExecuteNonQueryAsync();
        await using var verify = new VisualNotesDbContext(options);
        (await verify.Sessions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Permission_failure_during_capture_is_explicit_and_leaves_no_artifact()
    {
        var stream = new FailingReadStream(new UnauthorizedAccessException("permission denied"));
        await Should.ThrowAsync<UnauthorizedAccessException>(() => new ScreenshotStorageService(root, 0).StoreAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), stream)));
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static Task<StoredScreenshot> Store(ScreenshotStorageService service, byte[] bytes) =>
        service.StoreAsync(new(Guid.NewGuid(), Guid.NewGuid(), new MemoryStream(bytes)));
    private static byte[] CreatePng(int width, int height) { using var image = new Image<Rgba32>(width, height); using var output = new MemoryStream(); image.SaveAsPng(output); return output.ToArray(); }
    private static TextLanguageModelRequest Request(TimeSpan? timeout = null) => new("offline", new("offline", Timeout: timeout));
    private static OpenAiLanguageModelProvider Provider(HttpMessageHandler handler) => new(new HttpClient(handler), new OpenAiProviderOptions { ApiKey = "offline" });
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    private sealed class ResponseHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request, cancellationToken); }

    private sealed class FailingReadStream(Exception error) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw error;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(error);
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
