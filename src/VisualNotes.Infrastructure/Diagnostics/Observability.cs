using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Parsing;

namespace VisualNotes.Infrastructure.Diagnostics;

public sealed record CorrelationIds(Guid SessionId, Guid? CaptureId = null, Guid? JobId = null, Guid? CallId = null)
{
    public static CorrelationIds ForSession(Guid sessionId) => new(sessionId);
    public CorrelationIds WithCapture(Guid id) => this with { CaptureId = id };
    public CorrelationIds WithJob(Guid id) => this with { JobId = id };
    public CorrelationIds NewCall() => this with { CallId = Guid.NewGuid() };
}

public enum OperationBoundary { Capture, Storage, Api, Analysis, Crop, Export }

/// <summary>Shared local telemetry. It records only identifiers, status and duration; never payloads.</summary>
public sealed class VisualNotesTelemetry : IDisposable
{
    public const string SourceName = "VisualNotes";
    private readonly Meter meter = new(SourceName, "1.0");
    private readonly Histogram<double> duration;
    private readonly Counter<long> failures;
    private readonly TracerProvider tracerProvider;
    private readonly MeterProvider meterProvider;

    public VisualNotesTelemetry(bool enableLocalConsoleExporter = false)
    {
        duration = meter.CreateHistogram<double>("visualnotes.operation.duration", "ms");
        failures = meter.CreateCounter<long>("visualnotes.operation.failures");
        var traces = Sdk.CreateTracerProviderBuilder().AddSource(SourceName);
        var metrics = Sdk.CreateMeterProviderBuilder().AddMeter(SourceName);
        // Remote exporters are deliberately not registered. The optional console exporter stays local.
        if (enableLocalConsoleExporter) { traces.AddConsoleExporter(); metrics.AddConsoleExporter(); }
        tracerProvider = traces.Build();
        meterProvider = metrics.Build();
    }

    public async Task<T> MeasureAsync<T>(OperationBoundary boundary, CorrelationIds ids, Func<CancellationToken, Task<T>> action, ILogger logger, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var activity = VisualNotesActivity.Source.StartActivity(boundary.ToString(), ActivityKind.Internal);
        AddIdentifiers(activity, ids);
        var started = Stopwatch.GetTimestamp();
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["SessionId"] = ids.SessionId, ["CaptureId"] = ids.CaptureId, ["JobId"] = ids.JobId, ["CallId"] = ids.CallId });
        try
        {
            var result = await action(token).ConfigureAwait(false);
            Record(boundary, started, false);
            logger.LogInformation("{Operation} completed", boundary);
            return result;
        }
        catch (Exception error)
        {
            Record(boundary, started, true);
            activity?.SetStatus(ActivityStatusCode.Error, error.GetType().Name);
            logger.LogError(error, "{Operation} failed", boundary);
            throw; // A failed data boundary must never be treated as success.
        }
    }

    public Task MeasureAsync(OperationBoundary boundary, CorrelationIds ids, Func<CancellationToken, Task> action, ILogger logger, CancellationToken token = default) =>
        MeasureAsync(boundary, ids, async ct => { await action(ct).ConfigureAwait(false); return true; }, logger, token);

    private void Record(OperationBoundary boundary, long start, bool failed)
    {
        var tags = new TagList { { "operation", boundary.ToString().ToLowerInvariant() } };
        duration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, tags);
        if (failed) failures.Add(1, tags);
    }

    private static void AddIdentifiers(Activity? activity, CorrelationIds ids)
    {
        activity?.SetTag("session.id", ids.SessionId);
        activity?.SetTag("capture.id", ids.CaptureId);
        activity?.SetTag("job.id", ids.JobId);
        activity?.SetTag("call.id", ids.CallId);
    }

    public void Dispose() { tracerProvider.Dispose(); meterProvider.Dispose(); meter.Dispose(); }
}

public static class VisualNotesActivity { public static readonly ActivitySource Source = new(VisualNotesTelemetry.SourceName); }

/// <summary>Removes likely credentials and prohibited content from every event before it reaches a sink.</summary>
public sealed partial class RedactingSink(ILogEventSink inner) : ILogEventSink
{
    private const string Redacted = "[REDACTED]";
    public void Emit(LogEvent logEvent)
    {
        var properties = logEvent.Properties.ToDictionary(x => x.Key, x => IsSensitive(x.Key) ? new ScalarValue(Redacted) : Redact(x.Value));
        var message = SecretPattern().Replace(logEvent.MessageTemplate.Text, "$1=" + Redacted);
        inner.Emit(new LogEvent(logEvent.Timestamp, logEvent.Level, RedactException(logEvent.Exception), new MessageTemplateParser().Parse(message), properties));
    }
    private static bool IsSensitive(string name) => name.Contains("key", StringComparison.OrdinalIgnoreCase) || name.Contains("token", StringComparison.OrdinalIgnoreCase) || name.Contains("prompt", StringComparison.OrdinalIgnoreCase) || name.Contains("image", StringComparison.OrdinalIgnoreCase) || name.Contains("context", StringComparison.OrdinalIgnoreCase) || name.Contains("content", StringComparison.OrdinalIgnoreCase);
    private static LogEventPropertyValue Redact(LogEventPropertyValue value) => value switch
    {
        ScalarValue { Value: string text } => new ScalarValue(SecretPattern().Replace(text, "$1=" + Redacted)),
        SequenceValue sequence => new SequenceValue(sequence.Elements.Select(Redact)),
        StructureValue structure => new StructureValue(structure.Properties.Select(x => new LogEventProperty(x.Name, IsSensitive(x.Name) ? new ScalarValue(Redacted) : Redact(x.Value))), structure.TypeTag),
        DictionaryValue dictionary => new DictionaryValue(dictionary.Elements.ToDictionary(x => x.Key, x => Redact(x.Value))),
        _ => value
    };
    private static Exception? RedactException(Exception? error) => error is null ? null : new Exception($"{error.GetType().Name}: diagnostic details redacted");
    [GeneratedRegex("(?i)(api[_-]?key|authorization|bearer|token|secret|prompt|image|context)\\s*[:=]\\s*[^\\s,;]+")]
    private static partial Regex SecretPattern();
}

public static class LoggingFactory
{
    public static (ILoggerFactory Factory, Serilog.ILogger Logger) Create(string logPath, Action<LoggerConfiguration>? configure = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
        var configuration = new LoggerConfiguration().MinimumLevel.Information().Enrich.FromLogContext();
        configure?.Invoke(configuration);
        var file = new Serilog.Sinks.File.FileSink(logPath, new Serilog.Formatting.Json.JsonFormatter(), null);
        var logger = configuration.WriteTo.Sink(new RedactingSink(file)).CreateLogger();
        return (new SerilogLoggerFactory(logger, dispose: false), logger);
    }
}
