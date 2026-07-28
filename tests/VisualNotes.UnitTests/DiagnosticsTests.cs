using System.IO.Compression;

using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using Shouldly;

using VisualNotes.Infrastructure.Diagnostics;

namespace VisualNotes.UnitTests;

public sealed class DiagnosticsTests
{
    public static IEnumerable<object[]> Boundaries() => Enum.GetValues<OperationBoundary>().Select(x => new object[] { x });

    [Theory]
    [InlineData("Debug", LogEventLevel.Debug)]
    [InlineData("warning", LogEventLevel.Warning)]
    [InlineData("Error", LogEventLevel.Error)]
    [InlineData(null, LogEventLevel.Information)]
    [InlineData("not-a-level", LogEventLevel.Information)]
    public void Log_level_is_configurable_with_a_safe_default(string? value, LogEventLevel expected) =>
        LoggingFactory.ParseMinimumLevel(value).ShouldBe(expected);

    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task Every_boundary_records_and_rethrows_controlled_failures(OperationBoundary boundary)
    {
        using var telemetry = new VisualNotesTelemetry();
        using var factory = LoggerFactory.Create(_ => { });
        var expected = new ControlledBoundaryException();

        var actual = await Should.ThrowAsync<ControlledBoundaryException>(() => telemetry.MeasureAsync(
            boundary, CorrelationIds.ForSession(Guid.NewGuid()).WithCapture(Guid.NewGuid()).WithJob(Guid.NewGuid()).NewCall(),
            _ => Task.FromException(expected), factory.CreateLogger("test")));

        actual.ShouldBeSameAs(expected);
    }

    [Fact]
    public void Logs_redact_secrets_prompts_images_and_context()
    {
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(new RedactingSink(sink)).CreateLogger();
        logger.Information("apiKey={ApiKey} prompt={Prompt} image={Image} context={Context} authorization=Bearer-topsecret",
            "sk-secret-value", "private instructions", "base64pixels", "private notes");

        var output = sink.Events.Single().RenderMessage();
        output.ShouldNotBeNull().ShouldNotContain("sk-secret-value");
        output.ShouldNotContain("private instructions");
        output.ShouldNotContain("base64pixels");
        output.ShouldNotContain("private notes");
        output.ShouldContain("[REDACTED]");
    }

    [Fact]
    public void Logs_keep_safe_exception_structure_without_messages_or_paths()
    {
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(new RedactingSink(sink)).CreateLogger();

        try
        {
            ThrowSensitiveException();
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Controlled failure");
        }

        var diagnostic = sink.Events.Single().Exception!.ToString();
        diagnostic.ShouldContain(typeof(InvalidOperationException).FullName!);
        diagnostic.ShouldContain(nameof(ThrowSensitiveException));
        diagnostic.ShouldNotContain("private user content");
        diagnostic.ShouldNotContain("C:\\Users");
    }

    [Fact]
    public void Global_handler_exposes_friendly_error_and_keeps_technical_diagnostics()
    {
        var sink = new CollectingSink();
        using var serilog = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        using var factory = new Serilog.Extensions.Logging.SerilogLoggerFactory(serilog, false);
        UserFacingError? displayed = null;
        var handler = new GlobalExceptionHandler(factory.CreateLogger<GlobalExceptionHandler>(), x => displayed = x, _ => throw new InvalidOperationException());

        handler.HandleDispatcher(new ControlledBoundaryException("technical detail"));

        displayed.ShouldNotBeNull();
        displayed.Message.ShouldContain("Código de diagnóstico");
        displayed.Message.ShouldNotContain("technical detail");
        sink.Events.Single().Exception!.Message.ShouldBe("technical detail");
    }

    [Fact]
    public async Task Diagnostic_package_contains_only_anonymized_allow_list_data()
    {
        using var stream = new MemoryStream();
        await new DiagnosticPackageService().CreateAsync(stream, ["apiKey=secret", "prompt=private"]);
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.Entries.Select(x => x.FullName).ShouldBe(["environment.json", "events.json"], ignoreOrder: true);
        using var reader = new StreamReader(archive.GetEntry("events.json")!.Open());
        var content = await reader.ReadToEndAsync();
        content.ShouldNotContain("secret");
        content.ShouldNotContain("private");
        content.ShouldContain("redacted");
    }

    private sealed class ControlledBoundaryException(string? message = null) : Exception(message);
    private static void ThrowSensitiveException() =>
        throw new InvalidOperationException("private user content");

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
