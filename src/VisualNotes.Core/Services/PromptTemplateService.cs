using System.Text.Json;
using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

/// <summary>Versioned template operations suitable for an editor or portable backup.</summary>
public sealed class PromptTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Dictionary<Guid, PromptTemplateDefinition> templates;
    private readonly Dictionary<Guid, List<PromptTemplateDefinition>> history = [];

    public PromptTemplateService(IEnumerable<PromptTemplateDefinition>? initial = null) =>
        templates = (initial ?? DefaultPromptTemplates.All).ToDictionary(x => x.Id);

    public IReadOnlyList<PromptTemplateDefinition> List() => templates.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();

    public PromptTemplateDefinition Edit(Guid id, string name, string instructions)
    {
        var current = Get(id);
        SaveHistory(current);
        return templates[id] = current with { Name = Required(name), Instructions = Required(instructions), Version = current.Version + 1, IsDefault = false };
    }

    public PromptTemplateDefinition Duplicate(Guid id, string? name = null)
    {
        var source = Get(id);
        var copy = source with { Id = Guid.NewGuid(), Name = name ?? $"{source.Name} (copia)", Version = 1, IsDefault = false };
        templates.Add(copy.Id, copy);
        return copy;
    }

    public IReadOnlyList<PromptTemplateDifference> Compare(Guid leftId, Guid rightId)
    {
        var left = Get(leftId); var right = Get(rightId);
        return new[] { new PromptTemplateDifference("Name", left.Name, right.Name), new("Stage", left.Stage.ToString(), right.Stage.ToString()), new("Instructions", left.Instructions, right.Instructions) }
            .Where(x => !StringComparer.Ordinal.Equals(x.Left, x.Right)).ToArray();
    }

    public PromptTemplateDefinition Restore(Guid id, int version)
    {
        var current = Get(id);
        var source = history.GetValueOrDefault(id)?.SingleOrDefault(x => x.Version == version)
            ?? throw new KeyNotFoundException($"No existe la versión {version} de la plantilla.");
        SaveHistory(current);
        return templates[id] = source with { Version = current.Version + 1, IsDefault = false };
    }

    public string Export() => JsonSerializer.Serialize(new PromptTemplateExport(PromptTemplateExport.CurrentFormatVersion, List()), JsonOptions);

    public IReadOnlyList<PromptTemplateDefinition> Import(string json)
    {
        var data = JsonSerializer.Deserialize<PromptTemplateExport>(json, JsonOptions) ?? throw new JsonException("Documento de plantillas vacío.");
        if (data.FormatVersion != PromptTemplateExport.CurrentFormatVersion) throw new JsonException("Versión de plantillas no compatible.");
        foreach (var item in data.Templates)
        {
            var imported = item with { Id = templates.ContainsKey(item.Id) ? Guid.NewGuid() : item.Id, Version = Math.Max(1, item.Version), IsDefault = false, Name = Required(item.Name), Instructions = Required(item.Instructions) };
            templates.Add(imported.Id, imported);
        }
        return List();
    }

    private PromptTemplateDefinition Get(Guid id) => templates.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("Plantilla no encontrada.");
    private void SaveHistory(PromptTemplateDefinition value) { if (!history.TryGetValue(value.Id, out var versions)) history[value.Id] = versions = []; versions.Add(value); }
    private static string Required(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("El valor no puede estar vacío.") : value.Trim();
}

public static class DefaultPromptTemplates
{
    public static readonly PromptTemplateDefinition ExtractText = new(new("20c1a394-f3ad-4511-a51d-0a2d41f87b2d"), "Extracción fiel", PromptStage.Extraction, 1, "Extrae literalmente el contenido visible y localiza sus regiones.", true);
    public static readonly PromptTemplateDefinition StructuredNotes = new(new("c03d7fb7-c936-4496-9067-8a168892686c"), "Apuntes estructurados", PromptStage.Composition, 1, "Organiza el contenido extraído como apuntes claros, conservando hechos y relaciones.", true);
    public static readonly PromptTemplateDefinition StudySummary = new(new("7d96ce23-cfcb-4a8f-a463-c8fb2f748635"), "Resumen de estudio", PromptStage.Composition, 1, "Sintetiza conceptos, definiciones y preguntas de repaso sin añadir conocimiento externo.", true);
    public static IReadOnlyList<PromptTemplateDefinition> All { get; } = [ExtractText, StructuredNotes, StudySummary];
}
