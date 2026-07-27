using Shouldly;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

public sealed class CaptureLibraryTests
{
    [Fact, Trait("Category", "Unit")]
    public void Filters_compose_section_tag_status_importance_and_review()
    {
        var section = Guid.NewGuid();
        var matching = Capture(section, "diagram, exam", ScreenshotStatus.NeedsReview, CaptureImportance.Critical);
        var library = new CaptureLibrary([matching, Capture(Guid.NewGuid(), "exam", ScreenshotStatus.Ready, CaptureImportance.Critical)]);

        var result = library.Query(new(section, "DIAGRAM", ScreenshotStatus.NeedsReview, CaptureImportance.Critical, ReviewFilter.Pending));

        result.ShouldBe([matching]);
    }

    [Fact, Trait("Category", "Unit")]
    public void Reorder_preserves_relative_order_of_a_multi_selection()
    {
        var captures = Enumerable.Range(0, 5).Select(i => Capture(time: i)).ToArray();
        var library = new CaptureLibrary(captures);

        library.Reorder([captures[1].Id, captures[3].Id], 0);

        library.Captures.Select(x => x.Id).ShouldBe([captures[1].Id, captures[3].Id, captures[0].Id, captures[2].Id, captures[4].Id]);
    }

    [Fact, Trait("Category", "Unit")]
    public void Batch_commands_are_atomic_and_undo_restores_every_field_and_order()
    {
        var section = Guid.NewGuid();
        var captures = Enumerable.Range(0, 3).Select(i => Capture(time: i)).ToArray();
        var library = new CaptureLibrary(captures);
        var ids = captures.Take(2).Select(x => x.Id).ToArray();

        library.Exclude(ids);
        captures.Take(2).ShouldAllBe(x => x.ProcessingStatus == ScreenshotStatus.Excluded && !x.IncludeInDocument);
        library.Undo().ShouldBeTrue();

        captures.ShouldAllBe(x => x.ProcessingStatus == ScreenshotStatus.Ready && x.IncludeInDocument);
        library.MoveToSection(ids, section);
        captures.Take(2).ShouldAllBe(x => x.SectionId == section);
        library.Undo().ShouldBeTrue();
        captures.ShouldAllBe(x => x.SectionId is null);
    }

    [Theory, Trait("Category", "Unit")]
    [InlineData("delete")]
    [InlineData("restore")]
    [InlineData("reprocess")]
    public void Commands_apply_to_the_complete_selection(string command)
    {
        var captures = Enumerable.Range(0, 4).Select(i => Capture(time: i)).ToArray();
        var library = new CaptureLibrary(captures);
        var ids = captures.Skip(1).Take(2).Select(x => x.Id).ToArray();
        if (command == "delete") library.Delete(ids);
        if (command == "restore") { library.Delete(ids); library.Restore(ids); }
        if (command == "reprocess") library.Reprocess(ids);
        captures[0].ProcessingStatus.ShouldBe(ScreenshotStatus.Ready);
        captures[3].ProcessingStatus.ShouldBe(ScreenshotStatus.Ready);
        if (command == "delete") captures[1].Status.ShouldBe(EntityStatus.Deleted);
        if (command == "restore") captures[1].Status.ShouldBe(EntityStatus.Active);
        if (command == "reprocess") captures[1].ProcessingStatus.ShouldBe(ScreenshotStatus.Queued);
    }

    private static Screenshot Capture(Guid? section = null, string tags = "", ScreenshotStatus status = ScreenshotStatus.Ready, CaptureImportance importance = CaptureImportance.Normal, int time = 0) => new()
    { SectionId = section, Tags = tags, ProcessingStatus = status, Importance = importance, CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(time) };
}
