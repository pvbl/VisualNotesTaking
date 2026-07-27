using System.Text.Json;
using VisualNotes.Core.Services;

namespace VisualNotes.Infrastructure;

public sealed class JsonSettingsStore(string? filePath = null) : ISettingsStore
{
    private readonly string _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualNotes", "settings.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var values = await ReadAsync(cancellationToken);
            return values.TryGetValue(key, out var value) ? value.Deserialize<T>() : default;
        }
        finally { _gate.Release(); }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var values = await ReadAsync(cancellationToken);
            values[key] = JsonSerializer.SerializeToElement(value);
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, values, cancellationToken: cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, JsonElement>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return [];
        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(stream, cancellationToken: cancellationToken) ?? [];
    }
}
