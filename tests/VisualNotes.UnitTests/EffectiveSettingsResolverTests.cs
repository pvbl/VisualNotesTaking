using FsCheck.Xunit;
using Shouldly;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

public sealed class EffectiveSettingsResolverTests
{
    private static readonly SettingsValues Defaults = Values("default", true, 100);

    public static IEnumerable<object[]> EveryPrecedenceCombination()
    {
        for (var mask = 0; mask < 16; mask++)
        {
            var expected = (mask & 8) != 0 ? SettingsLevel.ScreenshotOverride
                : (mask & 4) != 0 ? SettingsLevel.Section
                : (mask & 2) != 0 ? SettingsLevel.Session
                : (mask & 1) != 0 ? SettingsLevel.Global
                : SettingsLevel.ApplicationDefaults;
            yield return [mask, expected];
        }
    }

    [Theory]
    [MemberData(nameof(EveryPrecedenceCombination))]
    public void Every_combination_uses_the_highest_explicit_level(int mask, SettingsLevel expected)
    {
        var context = new SettingsResolutionContext(
            Defaults,
            Layer(mask, 1, "global"),
            Layer(mask, 2, "session"),
            Layer(mask, 4, "section"),
            Layer(mask, 8, "screenshot"));

        var result = new EffectiveSettingsResolver().Resolve(context);

        result.Language.Source.ShouldBe(expected);
        result.Provider.Source.ShouldBe(expected);
        result.Model.Source.ShouldBe(expected);
        result.PromptTemplate.Source.ShouldBe(expected);
        result.IncludeImages.Source.ShouldBe(expected);
        result.MaximumImageSide.Source.ShouldBe(expected);
        result.Language.Value.ShouldBe(expected == SettingsLevel.ApplicationDefaults ? "default" : expected.ToString().Replace("Override", "").ToLowerInvariant());
    }

    [Fact]
    public void Restore_returns_a_setting_to_inheritance()
    {
        var global = Values("global", false, 200);
        var session = new SettingsValues().OverrideLanguage("session").InheritLanguage();

        new EffectiveSettingsResolver().Resolve(new(Defaults, global, session)).Language
            .ShouldBe(new EffectiveSetting<string>("global", SettingsLevel.Global));
    }

    [Property(MaxTest = 250)]
    public bool Resolving_the_same_configuration_is_deterministic(int mask, string? seed)
    {
        var text = seed ?? string.Empty;
        var context = new SettingsResolutionContext(
            Defaults with { Language = text }, Layer(mask, 1, text + "g"),
            Layer(mask, 2, text + "s"), Layer(mask, 4, text + "x"), Layer(mask, 8, text + "c"));
        var resolver = new EffectiveSettingsResolver();

        return resolver.Resolve(context) == resolver.Resolve(context);
    }

    private static SettingsValues? Layer(int mask, int bit, string value) =>
        (mask & bit) == 0 ? null : Values(value, bit % 2 == 0, bit * 100);

    private static SettingsValues Values(string value, bool includeImages, int maximumSide) => new()
    {
        Language = value, Provider = value, Model = value, PromptTemplate = value,
        IncludeImages = includeImages, MaximumImageSide = maximumSide
    };
}
