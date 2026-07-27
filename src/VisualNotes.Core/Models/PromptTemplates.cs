using System.Text.Json.Serialization;

namespace VisualNotes.Core.Models;

public enum PromptStage { Extraction, Composition }

public enum PromptScope { Course, Session, Section, Capture }

/// <summary>An editable instruction template. Invariants are supplied by the composer, not stored here.</summary>
public sealed record PromptTemplateDefinition(
    Guid Id,
    string Name,
    PromptStage Stage,
    int Version,
    string Instructions,
    bool IsDefault = false);

public sealed record ScopedPromptInstruction(PromptScope Scope, string Instructions);

public sealed record PromptCompositionRequest(
    PromptTemplateDefinition Template,
    string? Course = null,
    string? Session = null,
    string? Section = null,
    string? Capture = null);

public sealed record PromptWarning(string Code, string Message, PromptScope? Scope = null);

public sealed record EffectivePrompt(
    Guid TemplateId,
    int TemplateVersion,
    PromptStage Stage,
    string Text,
    IReadOnlyList<ScopedPromptInstruction> AppliedInstructions,
    IReadOnlyList<PromptWarning> Warnings);

/// <summary>Immutable audit copy persisted with an analysis.</summary>
public sealed record EffectivePromptSnapshot(
    Guid TemplateId,
    int TemplateVersion,
    PromptStage Stage,
    string EffectiveText,
    DateTimeOffset CreatedAt);

public sealed record PromptTemplateDifference(string Property, string Left, string Right);

public sealed record PromptTemplateExport(
    int FormatVersion,
    IReadOnlyList<PromptTemplateDefinition> Templates)
{
    [JsonIgnore]
    public const int CurrentFormatVersion = 1;
}
