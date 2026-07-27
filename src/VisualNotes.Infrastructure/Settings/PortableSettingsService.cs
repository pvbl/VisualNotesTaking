using System.Text.Json;
using VisualNotes.Core.Models;

namespace VisualNotes.Infrastructure.Settings;

/// <summary>Creates portable settings documents. Local secret references are always removed.</summary>
public sealed class PortableSettingsService(SettingsSchemaMigrator? migrator = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SettingsSchemaMigrator _migrator = migrator ?? new();

    public string Export(SettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var portable = document with { SchemaVersion = SettingsDocument.CurrentSchemaVersion, Secrets = [] };
        return JsonSerializer.Serialize(portable, Options);
    }

    public SettingsDocument Import(string json)
    {
        var imported = _migrator.Migrate(json);
        return imported with { Secrets = [] };
    }
}
