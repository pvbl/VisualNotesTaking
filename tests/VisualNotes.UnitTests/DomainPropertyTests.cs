using System.Text.Json;

using FsCheck;
using FsCheck.Xunit;

using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.Settings;

namespace VisualNotes.UnitTests;

[Trait("Category", "Unit")]
public sealed class DomainPropertyTests
{
    [Property(MaxTest = 1000, QuietOnSuccess = true)]
    public bool Normalized_boxes_are_order_independent(int x1, int y1, int x2, int y2)
    {
        x1 %= 1_000_000; x2 %= 1_000_000; y1 %= 1_000_000; y2 %= 1_000_000;
        var forward = ScreenCaptureGeometry.Normalize(x1, y1, x2, y2);
        var reverse = ScreenCaptureGeometry.Normalize(x2, y2, x1, y1);
        return forward == reverse && forward.Width >= 0 && forward.Height >= 0;
    }

    [Property(MaxTest = 1000, QuietOnSuccess = true)]
    public bool Dpi_scaling_round_trip_is_within_half_a_pixel(short dipValue, PositiveInt dpiValue)
    {
        var dip = dipValue / 4d;
        var dpi = (uint)Math.Clamp(dpiValue.Get, 1, 960);
        var pixels = ScreenCaptureGeometry.DipToPhysical(dip, dpi);
        var roundTrip = ScreenCaptureGeometry.PhysicalToDip(pixels, dpi);
        return Math.Abs(roundTrip - dip) <= ScreenCaptureGeometry.DefaultDpi / dpi / 2d + 1e-9;
    }

    [Property(MaxTest = 1000, QuietOnSuccess = true)]
    public bool Export_file_names_are_portable_bounded_and_idempotent(NonNull<string> input, PositiveInt limitValue)
    {
        var limit = Math.Clamp(limitValue.Get, 1, 200);
        var sanitized = ExportFileName.Sanitize(input.Get, maximumLength: limit);
        const string invalid = "<>:\"/\\|?*";
        return sanitized.Length is > 0 && sanitized.Length <= limit &&
            !sanitized.Any(character => char.IsControl(character) || invalid.Contains(character)) &&
            sanitized == ExportFileName.Sanitize(sanitized, maximumLength: limit);
    }

    [Property(MaxTest = 500, QuietOnSuccess = true)]
    public bool Portable_configuration_round_trip_preserves_non_transient_values(NonNull<string> language, bool images, PositiveInt side)
    {
        var source = new SettingsDocument
        {
            Global = new SettingsValues { Language = language.Get, IncludeImages = images, MaximumImageSide = side.Get },
            Secrets = new Dictionary<string, string> { ["api-key"] = "transient" }
        };
        var service = new PortableSettingsService();
        var restored = service.Import(service.Export(source));
        return restored.Global == source.Global && restored.SchemaVersion == source.SchemaVersion && restored.Secrets.Count == 0;
    }

    [Property(MaxTest = 500, QuietOnSuccess = true)]
    public bool Sections_are_composed_in_order_then_by_stable_identifier(NonEmptyArray<int> orders)
    {
        var session = new NoteSession { Name = "property" };
        foreach (var pair in orders.Get.Take(50).Select((order, index) => (order: order % 100, id: GuidFrom(index))))
            session.Sections.Add(new NoteSection { Id = pair.id, Title = pair.id.ToString(), Order = pair.order });
        var document = new SemanticDocumentComposer().Compose(session, []);
        var actual = document.Sections.Select(section => Guid.Parse(section.Content)).ToArray();
        var expected = session.Sections.OrderBy(section => section.Order).ThenBy(section => section.Id).Select(section => section.Id).ToArray();
        return actual.SequenceEqual(expected);
    }

    [Fact]
    public void Json_round_trip_preserves_model_except_explicitly_transient_fields()
    {
        var model = new SettingsDocument
        {
            Global = new() { Language = "es", Provider = "local", Model = "vlm", IncludeImages = true, MaximumImageSide = 1600 },
            Sessions = new() { [Guid.Parse("11111111-1111-1111-1111-111111111111")] = new() { PromptTemplate = "fiel" } }
        };

        var restored = JsonSerializer.Deserialize<SettingsDocument>(JsonSerializer.Serialize(model)).ShouldNotBeNull();
        restored.SchemaVersion.ShouldBe(model.SchemaVersion);
        restored.Global.ShouldBe(model.Global);
        restored.Sessions.ShouldBe(model.Sessions);
        restored.Sections.ShouldBe(model.Sections);
        restored.ScreenshotOverrides.ShouldBe(model.ScreenshotOverrides);
    }

    private static Guid GuidFrom(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }
}
