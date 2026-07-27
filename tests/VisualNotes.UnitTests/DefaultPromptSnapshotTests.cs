using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

public sealed class DefaultPromptSnapshotTests
{
    [Fact]
    public Task Extraction_template_is_reviewable() => Verify(DefaultPromptTemplates.ExtractText);

    [Fact]
    public Task Structured_notes_template_is_reviewable() => Verify(DefaultPromptTemplates.StructuredNotes);

    [Fact]
    public Task Study_summary_template_is_reviewable() => Verify(DefaultPromptTemplates.StudySummary);

    private static Task Verify(VisualNotes.Core.Models.PromptTemplateDefinition template) =>
        Verifier.Verify(new PromptComposer().Compose(new(template)).Text);
}
