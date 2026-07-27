using System.Text;
using System.Text.RegularExpressions;
using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

/// <summary>Composes editable instructions while keeping the response contract immutable.</summary>
public sealed partial class PromptComposer
{
    public const string SchemaRule = "REGLA PROTEGIDA — ESQUEMA: Devuelve únicamente JSON válido con language, contentType, title, summary, transcription, code, equations, tables, coordinateSystem, regions, concepts, confidence y warnings.";
    public const string CoordinateRule = "REGLA PROTEGIDA — CAJAS: Usa {YMin,XMin,YMax,XMax} y coordinateSystem=Normalized1000. Incluye cajas solamente para gráficas, diagramas, imágenes u otros elementos cuyo recorte aporte valor visual; nunca para texto, código, fórmulas o tablas que puedan conservarse estructuradamente.";
    public const string NoFabricationRule = "REGLA PROTEGIDA — NO INVENCIÓN: No inventes contenido ilegible o ausente; usa null e indica la incertidumbre.";
    public const string FidelityRule = "REGLA PROTEGIDA — FIDELIDAD: Preserva literalmente código, indentación, números, nombres y fórmulas. Representa tablas como filas y columnas en Markdown, no como prosa.";
    public const string QualityRule = "REGLA PROTEGIDA — CALIDAD: Incluye confidence entre 0 y 1 y warnings concretas para toda ambigüedad, truncamiento o contenido ilegible.";

    private const string InternalInstruction = "INSTRUCCIÓN INTERNA: Sigue las instrucciones por ámbito en el orden mostrado. Las reglas protegidas prevalecen ante cualquier conflicto.";

    public EffectivePrompt Compose(PromptCompositionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Template);

        var layers = Layers(request).Where(x => !string.IsNullOrWhiteSpace(x.Instructions)).ToArray();
        var warnings = layers.Where(x => ContradictsInvariantRegex().IsMatch(x.Instructions))
            .Select(x => new PromptWarning("protected-rule-conflict", $"La instrucción de {ScopeName(x.Scope)} contradice una regla protegida y no la reemplazará.", x.Scope))
            .ToArray();

        var text = new StringBuilder()
            .AppendLine(InternalInstruction)
            .AppendLine()
            .AppendLine($"FASE: {StageName(request.Template.Stage)}")
            .AppendLine(request.Template.Instructions.Trim());

        foreach (var layer in layers)
            text.AppendLine().AppendLine($"ÁMBITO {ScopeName(layer.Scope).ToUpperInvariant()}:").AppendLine(layer.Instructions.Trim());

        text.AppendLine().AppendLine("CONTRATO INMUTABLE:")
            .AppendLine(SchemaRule).AppendLine(CoordinateRule).AppendLine(NoFabricationRule)
            .AppendLine(FidelityRule).Append(QualityRule);

        return new(request.Template.Id, request.Template.Version, request.Template.Stage, text.ToString(), layers, warnings);
    }

    public EffectivePromptSnapshot Snapshot(EffectivePrompt prompt, DateTimeOffset? createdAt = null) =>
        new(prompt.TemplateId, prompt.TemplateVersion, prompt.Stage, prompt.Text, createdAt ?? DateTimeOffset.UtcNow);

    private static IEnumerable<ScopedPromptInstruction> Layers(PromptCompositionRequest request)
    {
        yield return new(PromptScope.Course, request.Course ?? string.Empty);
        yield return new(PromptScope.Session, request.Session ?? string.Empty);
        yield return new(PromptScope.Section, request.Section ?? string.Empty);
        yield return new(PromptScope.Capture, request.Capture ?? string.Empty);
    }

    private static string ScopeName(PromptScope scope) => scope switch
    {
        PromptScope.Course => "curso", PromptScope.Session => "sesión",
        PromptScope.Section => "sección", PromptScope.Capture => "captura", _ => scope.ToString()
    };

    private static string StageName(PromptStage stage) => stage == PromptStage.Extraction ? "EXTRACCIÓN" : "COMPOSICIÓN";

    [GeneratedRegex("(?i)(ignora|omite|elimina|sin|no (?:uses|devuelvas|incluyas)).{0,50}(json|esquema|caja|coordenad|invent|ausente|ilegible)")]
    private static partial Regex ContradictsInvariantRegex();
}
