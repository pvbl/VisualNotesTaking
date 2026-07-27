using System.Text.Json;
using VerifyXunit;
using VisualNotes.Testing.Fixtures;
using Xunit;

namespace VisualNotes.UnitTests;

[Trait("Category", "Unit")]
public sealed class SnapshotTests
{
    [Fact]
    public Task Prompt_snapshot_is_reviewable() => Verifier.Verify("Analiza la captura y devuelve texto estructurado en español.");

    [Fact]
    public Task Normalized_json_snapshot_is_reviewable()
    {
        var normalized = JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>("{\"section\":\"Definición\",\"order\":1}"), new JsonSerializerOptions { WriteIndented = true });
        return Verifier.Verify(normalized).UseExtension("json");
    }

    [Fact]
    public Task Document_structure_snapshot_is_reviewable()
    {
        var document = TestData.Document();
        var structure = JsonSerializer.Serialize(new
        {
            document.Title,
            Sections = document.Sections.Select(x => new { x.Title, x.Order, x.Content })
        }, new JsonSerializerOptions { WriteIndented = true });
        return Verifier.Verify(structure).UseExtension("json");
    }
}
