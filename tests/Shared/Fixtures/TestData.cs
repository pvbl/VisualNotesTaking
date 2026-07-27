using VisualNotes.Core.Models;

namespace VisualNotes.Testing.Fixtures;

public static class TestData
{
    public static readonly DateTimeOffset Timestamp = new(2025, 1, 15, 10, 30, 0, TimeSpan.Zero);

    public static NoteSession Session() => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Álgebra lineal", Module = "Matrices",
        Topic = "Determinantes", StartedAt = Timestamp, CreatedAt = Timestamp, ModifiedAt = Timestamp
    };

    public static Screenshot Capture(Guid? sessionId = null) => new()
    {
        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), SessionId = sessionId ?? Session().Id,
        CapturedAt = Timestamp, Width = 1920, Height = 1080, PerceptualHash = "fixture-phash",
        Image = Image(), Context = new ScreenshotContext { WindowTitle = "Diapositivas", ApplicationName = "Fixture" }
    };

    public static ScreenshotImage Image() => new()
    {
        Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), RelativePath = "sessions/fixture/captures/slide.png",
        MediaType = "image/png", ByteLength = 68, Sha256 = "fixture-sha256"
    };

    public static ExtractedContent VlmResponse() => new("Una matriz cuadrada.", new Dictionary<string, string> { ["model"] = "fixture-vlm", ["language"] = "es" });

    public static ProviderProfile Configuration() => new() { Name = "Local fixture", Provider = "Fixture", Model = "vlm-test", Endpoint = "http://localhost.invalid" };

    public static ExportDocument Document() => new("Determinantes", [new NoteSection(Guid.Parse("44444444-4444-4444-4444-444444444444"), "Definición", "Contenido normalizado", 0)], Timestamp);
}
