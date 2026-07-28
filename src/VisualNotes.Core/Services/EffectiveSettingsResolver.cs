using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

/// <summary>Resolves every setting with an explicit, deterministic precedence.</summary>
public sealed class EffectiveSettingsResolver
{
    public EffectiveSettings Resolve(SettingsResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateDefaults(context.ApplicationDefaults);

        var layers = new (SettingsLevel Level, SettingsValues? Values)[]
        {
            (SettingsLevel.ScreenshotOverride, context.ScreenshotOverride),
            (SettingsLevel.Section, context.Section),
            (SettingsLevel.Session, context.Session),
            (SettingsLevel.Global, context.Global),
            (SettingsLevel.ApplicationDefaults, context.ApplicationDefaults)
        };

        return new(
            Resolve(layers, x => x.Language),
            Resolve(layers, x => x.Provider),
            Resolve(layers, x => x.Model),
            Resolve(layers, x => x.PromptTemplate),
            ResolveValue(layers, x => x.IncludeImages),
            ResolveValue(layers, x => x.MaximumImageSide));
    }

    private static EffectiveSetting<T> Resolve<T>(
        IEnumerable<(SettingsLevel Level, SettingsValues? Values)> layers,
        Func<SettingsValues, T?> selector)
    {
        foreach (var (level, values) in layers)
        {
            if (values is not null && selector(values) is { } value)
                return new(value, level);
        }

        throw new InvalidOperationException("Application defaults must define every setting.");
    }

    private static EffectiveSetting<T> ResolveValue<T>(
        IEnumerable<(SettingsLevel Level, SettingsValues? Values)> layers,
        Func<SettingsValues, T?> selector)
        where T : struct
    {
        foreach (var (level, values) in layers)
        {
            var value = values is null ? null : selector(values);
            if (value.HasValue)
                return new(value.Value, level);
        }

        throw new InvalidOperationException("Application defaults must define every setting.");
    }

    private static void ValidateDefaults(SettingsValues defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        if (defaults.Language is null || defaults.Provider is null || defaults.Model is null ||
            defaults.PromptTemplate is null || defaults.IncludeImages is null || defaults.MaximumImageSide is null)
            throw new ArgumentException("Application defaults must define every setting.", nameof(defaults));
    }
}

/// <summary>Operations used by editors to override a value or restore inheritance.</summary>
public static class SettingsInheritance
{
    public static SettingsValues OverrideLanguage(this SettingsValues values, string value) => values with { Language = value };
    public static SettingsValues InheritLanguage(this SettingsValues values) => values with { Language = null };
    public static SettingsValues OverrideProvider(this SettingsValues values, string value) => values with { Provider = value };
    public static SettingsValues InheritProvider(this SettingsValues values) => values with { Provider = null };
    public static SettingsValues OverrideModel(this SettingsValues values, string value) => values with { Model = value };
    public static SettingsValues InheritModel(this SettingsValues values) => values with { Model = null };
    public static SettingsValues OverridePromptTemplate(this SettingsValues values, string value) => values with { PromptTemplate = value };
    public static SettingsValues InheritPromptTemplate(this SettingsValues values) => values with { PromptTemplate = null };
    public static SettingsValues OverrideIncludeImages(this SettingsValues values, bool value) => values with { IncludeImages = value };
    public static SettingsValues InheritIncludeImages(this SettingsValues values) => values with { IncludeImages = null };
    public static SettingsValues OverrideMaximumImageSide(this SettingsValues values, int value) => values with { MaximumImageSide = value };
    public static SettingsValues InheritMaximumImageSide(this SettingsValues values) => values with { MaximumImageSide = null };
}
