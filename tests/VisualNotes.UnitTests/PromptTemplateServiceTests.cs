using System.Text.Json;
using Shouldly;
using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

public sealed class PromptTemplateServiceTests
{
    [Fact]
    public void Edit_duplicate_compare_and_restore_are_versioned()
    {
        var service = new PromptTemplateService();
        var original = DefaultPromptTemplates.ExtractText;
        var edited = service.Edit(original.Id, original.Name, "Nueva extracción");
        var copy = service.Duplicate(original.Id);

        edited.Version.ShouldBe(2);
        service.Compare(original.Id, copy.Id).ShouldBeEmpty();
        var restored = service.Restore(original.Id, 1);
        restored.Version.ShouldBe(3);
        restored.Instructions.ShouldBe(original.Instructions);
    }

    [Fact]
    public void Portable_export_has_no_secrets_paths_or_session_context()
    {
        var json = new PromptTemplateService().Export();

        json.ShouldNotContain("apiKey", Case.Insensitive);
        json.ShouldNotContain("path", Case.Insensitive);
        json.ShouldNotContain("session", Case.Insensitive);
    }

    [Fact]
    public void Import_rejects_unknown_format()
    {
        Should.Throw<JsonException>(() => new PromptTemplateService().Import("""{"formatVersion":99,"templates":[]}"""));
    }
}
