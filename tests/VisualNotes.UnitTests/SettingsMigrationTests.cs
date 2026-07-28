using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Infrastructure.Settings;

namespace VisualNotes.UnitTests;

public sealed class SettingsMigrationTests
{
    public static IEnumerable<object[]> EveryPublishedVersion() =>
    [
        ["""{"schemaVersion":1,"language":"es","provider":"local","model":"m1","promptTemplate":"p","includeImages":true,"maximumImageSide":1200}""", 1],
        ["""{"schemaVersion":2,"global":{"language":"es","provider":"local","model":"m2","promptTemplate":"p","includeImages":true,"maximumImageSide":1200},"sessions":{}}""", 2],
        ["""{"schemaVersion":3,"global":{"language":"es","provider":"local","model":"m3","promptTemplate":"p","includeImages":true,"maximumImageSide":1200},"sessions":{},"sections":{},"screenshotOverrides":{},"secrets":{}}""", 3]
    ];

    [Theory]
    [MemberData(nameof(EveryPublishedVersion))]
    public void Migrates_every_published_version_to_current(string json, int sourceVersion)
    {
        var result = new SettingsSchemaMigrator().Migrate(json);

        result.SchemaVersion.ShouldBe(SettingsDocument.CurrentSchemaVersion);
        result.Global.Language.ShouldBe("es");
        result.Global.Model.ShouldBe($"m{sourceVersion}");
        result.Sessions.ShouldNotBeNull();
        result.Sections.ShouldNotBeNull();
        result.ScreenshotOverrides.ShouldNotBeNull();
    }

    [Fact]
    public void Export_and_import_never_transfer_secrets()
    {
        var service = new PortableSettingsService();
        var source = new SettingsDocument
        {
            Global = new() { Language = "es" },
            Secrets = new() { ["providerApiKey"] = "super-secret-value" }
        };

        var exported = service.Export(source);
        var imported = service.Import(exported);

        exported.ShouldNotContain("super-secret-value");
        exported.ShouldNotContain("providerApiKey");
        imported.Secrets.ShouldBeEmpty();
        imported.Global.Language.ShouldBe("es");
    }

    [Fact]
    public void Rejects_unknown_future_versions() => Should.Throw<NotSupportedException>(() =>
        new SettingsSchemaMigrator().Migrate("{\"schemaVersion\":999}"));
}
