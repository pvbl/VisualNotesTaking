using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

using Xunit;

namespace VisualNotes.UnitTests;

[Trait("Category", "Unit")]
public sealed class CaptureEditingTests
{
    [Fact]
    public async Task Debounce_CoalescesRapidEdits()
    {
        var saved = new List<string>();
        var completed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var saver = new DebouncedCaptureSaver((capture, _) =>
        {
            saved.Add(capture.UserContext);
            completed.TrySetResult(capture.UserContext);
            return Task.CompletedTask;
        }, TimeSpan.FromMilliseconds(30));
        var capture = new Screenshot { UserContext = "primero" };
        saver.Schedule(capture);
        capture.UserContext = "último";
        saver.Schedule(capture);
        (await completed.Task.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe("último");
        saved.ShouldBe(["último"]);
    }

    [Fact]
    public async Task Flush_PersistsPartialTextBeforeShutdown()
    {
        string? saved = null;
        await using var saver = new DebouncedCaptureSaver((capture, _) => { saved = capture.UserContext; return Task.CompletedTask; }, TimeSpan.FromMinutes(1));
        saver.Schedule(new Screenshot { UserContext = "texto parcial" });
        await saver.FlushAsync();
        saved.ShouldBe("texto parcial");
    }

    [Fact]
    public void InstructionResolver_CombinesChipsAndFreeText()
    {
        var result = CaptureInstructionResolver.Resolve(["examen", "código completo"], "Usa C#");
        result.ShouldContain("preguntas de examen");
        result.ShouldContain("código completo");
        result.ShouldEndWith("Usa C#");
    }

    [Fact]
    public void Revision_IsAnImmutableValueSnapshotOfEditableFields()
    {
        var capture = new Screenshot { UserContext = "original", Tags = "duda", Importance = CaptureImportance.Important };
        var revision = new CaptureRevision { UserContext = capture.UserContext, Tags = capture.Tags, Importance = capture.Importance, RevisionNumber = 1 };
        capture.UserContext = "editado";
        revision.UserContext.ShouldBe("original");
        revision.RevisionNumber.ShouldBe(1);
    }
}
