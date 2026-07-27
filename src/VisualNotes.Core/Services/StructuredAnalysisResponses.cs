using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisualNotes.Core.Services;

public enum CoordinateSystem { Pixels, Normalized01, Normalized1000 }
public enum ContentBlockType { Text, Code, Equation, Table }

public sealed record BoundingBox(
    [property: JsonPropertyName("YMin")] double YMin,
    [property: JsonPropertyName("XMin")] double XMin,
    [property: JsonPropertyName("YMax")] double YMax,
    [property: JsonPropertyName("XMax")] double XMax);
public sealed record ContentBlock(ContentBlockType Type, string Content, BoundingBox? Box = null);
public sealed record AnalysisRegion(string Label, BoundingBox Box, double Confidence);

public sealed record StructuredAnalysisResponse(
    string Language,
    string ContentType,
    string Title,
    string Summary,
    string Transcription,
    IReadOnlyList<ContentBlock> Code,
    IReadOnlyList<ContentBlock> Equations,
    IReadOnlyList<ContentBlock> Tables,
    CoordinateSystem CoordinateSystem,
    IReadOnlyList<AnalysisRegion> Regions,
    IReadOnlyList<string> Concepts,
    double Confidence,
    IReadOnlyList<string> Warnings);

public sealed record AnalysisResponsePrivacy(bool RetainOriginalResponse = true);

/// <summary>A response that has passed schema validation and is safe to persist.</summary>
public sealed record ValidatedAnalysisResponse(
    StructuredAnalysisResponse Value,
    string? OriginalJson,
    string NormalizedJson);

public sealed class AnalysisResponseValidationException : Exception
{
    public AnalysisResponseValidationException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}

/// <summary>Strict, bounded parser for untrusted language-model JSON.</summary>
public static class StructuredAnalysisResponseParser
{
    public const int MaximumPayloadBytes = 1_048_576;
    private const int _maximumItems = 1_000;
    private static readonly JsonSerializerOptions _jsonOptions = CreateJsonOptions();

    public static ValidatedAnalysisResponse Parse(string json, AnalysisResponsePrivacy? privacy = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumPayloadBytes)
            throw new AnalysisResponseValidationException($"Response exceeds the {MaximumPayloadBytes}-byte limit.");

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Invalid("The response root must be an object.");

            var coordinateSystem = EnumValue<CoordinateSystem>(RequiredString(root, "coordinateSystem"), "coordinateSystem");
            var value = new StructuredAnalysisResponse(
                RequiredString(root, "language"), RequiredString(root, "contentType"),
                RequiredString(root, "title"), RequiredString(root, "summary"),
                RequiredString(root, "transcription"), Blocks(root, "code", ContentBlockType.Code),
                Blocks(root, "equations", ContentBlockType.Equation),
                Blocks(root, "tables", ContentBlockType.Table), coordinateSystem,
                Regions(root), Strings(root, "concepts"), Confidence(root, "confidence"),
                Strings(root, "warnings"));

            var normalized = JsonSerializer.Serialize(value, _jsonOptions);
            return new(value, (privacy ?? new()).RetainOriginalResponse ? json : null, normalized);
        }
        catch (AnalysisResponseValidationException) { throw; }
        catch (JsonException exception)
        {
            throw new AnalysisResponseValidationException("Response is not valid JSON.", exception);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or OverflowException)
        {
            throw new AnalysisResponseValidationException("Response contains an invalid value.", exception);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static IReadOnlyList<ContentBlock> Blocks(JsonElement root, string name, ContentBlockType expected)
    {
        var array = RequiredArray(root, name);
        var result = new List<ContentBlock>();
        foreach (var item in Bounded(array, name))
        {
            RequireObject(item, name);
            var type = EnumValue<ContentBlockType>(RequiredString(item, "type"), $"{name}.type");
            if (type != expected) throw Invalid($"Items in '{name}' must have type '{expected}'.");
            result.Add(new(type, RequiredString(item, "content"), OptionalBox(item)));
        }
        return result;
    }

    private static IReadOnlyList<AnalysisRegion> Regions(JsonElement root)
    {
        var result = new List<AnalysisRegion>();
        foreach (var item in Bounded(RequiredArray(root, "regions"), "regions"))
        {
            RequireObject(item, "regions");
            result.Add(new(RequiredString(item, "label"), RequiredBox(item), Confidence(item, "confidence")));
        }
        return result;
    }

    private static IReadOnlyList<string> Strings(JsonElement root, string name) =>
        Bounded(RequiredArray(root, name), name).Select((item, index) => item.ValueKind == JsonValueKind.String
            ? item.GetString()! : throw Invalid($"'{name}[{index}]' must be a string.")).ToArray();

    private static BoundingBox? OptionalBox(JsonElement item) =>
        item.TryGetProperty("box", out var box) && box.ValueKind != JsonValueKind.Null ? Box(box) : null;
    private static BoundingBox RequiredBox(JsonElement item) =>
        item.TryGetProperty("box", out var box) ? Box(box) : throw Invalid("Required property 'box' is missing.");

    private static BoundingBox Box(JsonElement element)
    {
        RequireObject(element, "box");
        var box = new BoundingBox(Number(element, "YMin"), Number(element, "XMin"), Number(element, "YMax"), Number(element, "XMax"));
        if (box.YMin >= box.YMax || box.XMin >= box.XMax)
            throw Invalid("Box coordinates are empty or inverted.");
        return box;
    }

    private static double Confidence(JsonElement root, string name)
    {
        var value = Number(root, name);
        if (value is < 0 or > 1) throw Invalid($"'{name}' must be between 0 and 1.");
        return value;
    }

    private static double Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var item)) throw Invalid($"Required property '{name}' is missing.");
        if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var value) || !double.IsFinite(value))
            throw Invalid($"'{name}' must be a finite number.");
        return value;
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var item)) throw Invalid($"Required property '{name}' is missing.");
        if (item.ValueKind != JsonValueKind.String) throw Invalid($"'{name}' must be a string.");
        var value = item.GetString()!;
        if (string.IsNullOrWhiteSpace(value)) throw Invalid($"'{name}' cannot be empty.");
        return value;
    }

    private static JsonElement.ArrayEnumerator RequiredArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var item)) throw Invalid($"Required property '{name}' is missing.");
        if (item.ValueKind != JsonValueKind.Array) throw Invalid($"'{name}' must be an array.");
        return item.EnumerateArray();
    }

    private static IEnumerable<JsonElement> Bounded(JsonElement.ArrayEnumerator values, string name)
    {
        var count = 0;
        foreach (var value in values)
        {
            if (++count > _maximumItems) throw Invalid($"'{name}' contains too many items.");
            yield return value;
        }
    }

    private static T EnumValue<T>(string value, string name) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : throw Invalid($"'{name}' has an unsupported value.");
    private static void RequireObject(JsonElement value, string name)
    { if (value.ValueKind != JsonValueKind.Object) throw Invalid($"'{name}' items must be objects."); }
    private static AnalysisResponseValidationException Invalid(string message) => new(message);
}
