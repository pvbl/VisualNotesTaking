namespace VisualNotes.Core.Models;

/// <summary>Configuration levels in ascending order of precedence.</summary>
public enum SettingsLevel
{
    ApplicationDefaults,
    Global,
    Session,
    Section,
    ScreenshotOverride
}

/// <summary>
/// Values owned by a single level. A null value means "inherit from the next
/// lower level"; it is deliberately different from an empty string.
/// </summary>
public sealed record SettingsValues
{
    public string? Language { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? PromptTemplate { get; init; }
    public bool? IncludeImages { get; init; }
    public int? MaximumImageSide { get; init; }
}

public sealed record EffectiveSetting<T>(T Value, SettingsLevel Source)
{
    public string Provenance => Source switch
    {
        SettingsLevel.ApplicationDefaults => "Predeterminado de la aplicación",
        SettingsLevel.Global => "Global",
        SettingsLevel.Session => "Sesión",
        SettingsLevel.Section => "Sección",
        SettingsLevel.ScreenshotOverride => "Captura",
        _ => Source.ToString()
    };
}

public sealed record EffectiveSettings(
    EffectiveSetting<string> Language,
    EffectiveSetting<string> Provider,
    EffectiveSetting<string> Model,
    EffectiveSetting<string> PromptTemplate,
    EffectiveSetting<bool> IncludeImages,
    EffectiveSetting<int> MaximumImageSide);

/// <summary>The complete resolution context, from defaults to screenshot override.</summary>
public sealed record SettingsResolutionContext(
    SettingsValues ApplicationDefaults,
    SettingsValues? Global = null,
    SettingsValues? Session = null,
    SettingsValues? Section = null,
    SettingsValues? ScreenshotOverride = null);

/// <summary>Versioned settings persisted by the application (current schema: v3).</summary>
public sealed record SettingsDocument
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public SettingsValues Global { get; init; } = new();
    public Dictionary<Guid, SettingsValues> Sessions { get; init; } = [];
    public Dictionary<Guid, SettingsValues> Sections { get; init; } = [];
    public Dictionary<Guid, SettingsValues> ScreenshotOverrides { get; init; } = [];

    /// <summary>
    /// Secret references are local-only. They are never included by the portable
    /// import/export service.
    /// </summary>
    public Dictionary<string, string> Secrets { get; init; } = [];
}
