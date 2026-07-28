using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

public static class CaptureInstructionResolver
{
    private static readonly IReadOnlyDictionary<string, string> Instructions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["examen"] = "Prioriza conceptos evaluables y posibles preguntas de examen.",
        ["duda"] = "Marca la información como pendiente de aclaración.",
        ["definición"] = "Extrae una definición precisa y sus términos clave.",
        ["ejemplo"] = "Conserva el ejemplo y explica qué concepto ilustra.",
        ["conservar gráfica"] = "Incluye la gráfica y describe ejes, leyenda y tendencia.",
        ["transcripción literal"] = "Transcribe literalmente, sin resumir ni parafrasear.",
        ["código completo"] = "Conserva el código completo respetando formato e indentación."
    };

    public static IReadOnlyCollection<string> QuickChips { get; } = Instructions.Keys.ToArray();

    public static string Resolve(IEnumerable<string> chips, string instruction)
    {
        var resolved = chips.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(chip => Instructions.TryGetValue(chip, out var value) ? value : chip)
            .Append(instruction.Trim()).Where(value => !string.IsNullOrWhiteSpace(value));
        return string.Join(Environment.NewLine, resolved);
    }
}

/// <summary>Coalesces rapid edits, while allowing shutdown to flush the latest draft.</summary>
public sealed class DebouncedCaptureSaver(Func<Screenshot, CancellationToken, Task> save, TimeSpan delay) : IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private Task _saveTask = Task.CompletedTask;
    private Screenshot? _latest;

    public void Schedule(Screenshot capture)
    {
        lock (_gate)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = new CancellationTokenSource();
            _latest = capture;
            _saveTask = SaveLaterAsync(capture, _pending.Token);
        }
    }

    public async Task FlushAsync()
    {
        Task pending; Screenshot? latest;
        CancellationTokenSource? pendingCancellation;
        lock (_gate) { pendingCancellation = _pending; pending = _saveTask; latest = _latest; _latest = null; }
        if (pendingCancellation is not null) await pendingCancellation.CancelAsync().ConfigureAwait(false);
        try { await pending.ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            // The scheduled save was intentionally cancelled so the latest draft can be flushed immediately.
        }
        if (latest is not null) await save(latest, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task SaveLaterAsync(Screenshot capture, CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        await save(capture, cancellationToken).ConfigureAwait(false);
        lock (_gate) { if (ReferenceEquals(_latest, capture)) _latest = null; }
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAsync().ConfigureAwait(false);
        _pending?.Dispose();
    }
}
