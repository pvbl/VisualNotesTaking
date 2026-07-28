using System.Text.Encodings.Web;
using System.Text.Json;

using FsCheck.Xunit;

using Shouldly;

using VerifyXunit;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

using Xunit;

namespace VisualNotes.UnitTests;

[Trait("Category", "Unit")]
public sealed class StructuredAnalysisResponseTests
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private const string ValidJson = """
        {
          "language": "es",
          "contentType": "lecture-slide",
          "title": "Ley de Ohm",
          "summary": "Relación entre voltaje, corriente y resistencia.",
          "transcription": "V = I R",
          "code": [{"type":"Code","content":"var voltage = current * resistance;","box":{"YMin":100,"XMin":50,"YMax":180,"XMax":700}}],
          "equations": [{"type":"Equation","content":"V = I R"}],
          "tables": [{"type":"Table","content":"V | I | R"}],
          "coordinateSystem": "Normalized1000",
          "regions": [{"label":"formula","box":{"YMin":200,"XMin":100,"YMax":400,"XMax":900},"confidence":0.98}],
          "concepts": ["voltaje", "resistencia"],
          "confidence": 0.95,
          "warnings": ["La unidad de R no es visible"]
        }
        """;

    [Fact]
    public void Valid_schema_is_normalized_and_can_be_persisted()
    {
        var parsed = StructuredAnalysisResponseParser.Parse(ValidJson);
        parsed.Value.CoordinateSystem.ShouldBe(CoordinateSystem.Normalized1000);
        parsed.Value.Regions[0].Box.ShouldBe(new BoundingBox(200, 100, 400, 900));

        var analysis = new CaptureAnalysis();
        analysis.SetStructuredResponse(parsed);
        analysis.OriginalResponseJson.ShouldBe(ValidJson);
        analysis.NormalizedResponseJson.ShouldBe(parsed.NormalizedJson);
    }

    [Theory]
    [InlineData("\"language\": \"es\",", "")]
    [InlineData("\"confidence\": 0.95", "\"confidence\": \"high\"")]
    [InlineData("\"confidence\": 0.95", "\"confidence\": 1e400")]
    [InlineData("\"YMin\":200,\"XMin\":100,\"YMax\":400,\"XMax\":900", "\"YMin\":400,\"XMin\":100,\"YMax\":200,\"XMax\":900")]
    public void Invalid_schema_is_rejected(string oldValue, string newValue)
    {
        var json = ValidJson.Replace(oldValue, newValue, StringComparison.Ordinal);
        Should.Throw<AnalysisResponseValidationException>(() => StructuredAnalysisResponseParser.Parse(json));
    }

    [Fact]
    public void Excessive_payload_is_rejected_before_json_parsing()
    {
        var payload = new string('x', StructuredAnalysisResponseParser.MaximumPayloadBytes + 1);
        Should.Throw<AnalysisResponseValidationException>(() => StructuredAnalysisResponseParser.Parse(payload))
            .Message.ShouldContain("exceeds");
    }

    [Fact]
    public void Privacy_policy_can_discard_original_but_keeps_normalized_response()
    {
        var parsed = StructuredAnalysisResponseParser.Parse(ValidJson, new(false));
        parsed.OriginalJson.ShouldBeNull();
        parsed.NormalizedJson.ShouldContain("\"language\": \"es\"");
    }

    [Property(MaxTest = 500)]
    public bool Arbitrary_input_never_leaks_parser_exceptions(string? input)
    {
        try
        {
            _ = StructuredAnalysisResponseParser.Parse(input ?? string.Empty);
            return true;
        }
        catch (AnalysisResponseValidationException)
        {
            return true;
        }
    }

    [Fact]
    public Task Representative_normalized_result_is_reviewable()
    {
        var normalized = StructuredAnalysisResponseParser.Parse(ValidJson, new(false)).NormalizedJson;
        var reviewable = JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(normalized), SnapshotJsonOptions);
        return Verifier.Verify(reviewable, extension: "json");
    }
}
