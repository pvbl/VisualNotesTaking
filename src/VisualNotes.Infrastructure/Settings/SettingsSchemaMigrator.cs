using System.Text.Json;
using System.Text.Json.Nodes;

using VisualNotes.Core.Models;

namespace VisualNotes.Infrastructure.Settings;

public sealed class SettingsSchemaMigrator
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public SettingsDocument Migrate(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new JsonException("The settings document must be a JSON object.");
        var version = root["schemaVersion"]?.GetValue<int>() ?? 1;
        if (version < 1 || version > SettingsDocument.CurrentSchemaVersion)
            throw new NotSupportedException($"Settings schema version {version} is not supported.");

        while (version < SettingsDocument.CurrentSchemaVersion)
        {
            root = version switch
            {
                1 => MigrateV1ToV2(root),
                2 => MigrateV2ToV3(root),
                _ => throw new NotSupportedException($"No migration exists for version {version}.")
            };
            version++;
        }

        return root.Deserialize<SettingsDocument>(Options)
            ?? throw new JsonException("The settings document could not be deserialized.");
    }

    private static JsonObject MigrateV1ToV2(JsonObject source)
    {
        var global = new JsonObject();
        Move(source, global, "language");
        Move(source, global, "provider");
        Move(source, global, "model");
        Move(source, global, "promptTemplate");
        Move(source, global, "includeImages");
        Move(source, global, "maximumImageSide");
        source["global"] = global;
        source["sessions"] ??= new JsonObject();
        source["schemaVersion"] = 2;
        return source;
    }

    private static JsonObject MigrateV2ToV3(JsonObject source)
    {
        source["sections"] ??= new JsonObject();
        source["screenshotOverrides"] ??= new JsonObject();
        source["secrets"] ??= new JsonObject();
        source["schemaVersion"] = 3;
        return source;
    }

    private static void Move(JsonObject source, JsonObject destination, string name)
    {
        if (!source.TryGetPropertyValue(name, out var value)) return;
        source.Remove(name);
        destination[name] = value;
    }
}
