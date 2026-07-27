using System.Text;
using System.Text.RegularExpressions;
using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

/// <summary>Composes editable instructions while keeping the response contract immutable.</summary>
public sealed partial class PromptComposer
{
    public const string SchemaRule = "REGLA PROTEGIDA — ESQUEMA: Devuelve JSON válido con las propiedades text, summary y boxes.";
    public const string CoordinateRule = "REGLA PROTEGIDA — CAJAS: Cada caja usa {x,y,width,height} en píxeles de la imagen original, con origen (0,0) arriba a la izquierda.";
    public const string NoFabricationRule = "REGLA PROTEGIDA — NO INVENCIÓN: No inventes contenido ilegible o ausente; usa null e indica la incertidumbre.";

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
            .AppendLine(SchemaRule).AppendLine(CoordinateRule).Append(NoFabricationRule);

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
