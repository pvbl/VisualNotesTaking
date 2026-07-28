using Shouldly;
using VisualNotes.App.ViewModels;
using VisualNotes.Core.Models;

namespace VisualNotes.UiTests;

public sealed class CaptureManagementUiTests
{
    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Extended_selection_runs_destructive_action_and_keyboard_undo()
    {
        var captures = MakeCaptures();
        var viewModel = new CapturesViewModel(captures);
        viewModel.ReplaceSelection(captures.Take(2));

        viewModel.DeleteCommand.Execute(null); // bound to Delete
        captures.Take(2).ShouldAllBe(capture => capture.Status == EntityStatus.Deleted);
        viewModel.SelectionCount.ShouldBe(2);

        viewModel.UndoCommand.Execute(null); // bound to Ctrl+Z
        captures.Take(2).ShouldAllBe(capture => capture.Status == EntityStatus.Active);
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Drag_and_drop_command_reorders_the_selected_rows()
    {
        var captures = MakeCaptures();
        var viewModel = new CapturesViewModel(captures);
        viewModel.ReplaceSelection([captures[1], captures[2]]);

        viewModel.ReorderCommand.Execute(0);

        viewModel.Captures.Select(capture => capture.Id).ShouldBe([captures[1].Id, captures[2].Id, captures[0].Id]);
        viewModel.UndoCommand.Execute(null);
        viewModel.Captures.Select(capture => capture.Id).ShouldBe(captures.Select(capture => capture.Id));
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Filters_update_the_virtualized_items_source()
    {
        var captures = MakeCaptures();
        captures[0].Tags = "diagram";
        var viewModel = new CapturesViewModel(captures);

        viewModel.TagFilter = "diagram";

        viewModel.Captures.ShouldBe([captures[0]]);
        viewModel.ClearFiltersCommand.Execute(null);
        viewModel.Captures.Count.ShouldBe(3);
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Newly_completed_capture_appears_in_capture_view()
    {
        var viewModel = new CapturesViewModel();
        var capture = new Screenshot { CapturedAt = DateTimeOffset.UtcNow, ProcessingStatus = ScreenshotStatus.Captured };

        viewModel.AddCapture(capture);

        viewModel.Captures.ShouldHaveSingleItem().ShouldBeSameAs(capture);
    }

    private static Screenshot[] MakeCaptures() => Enumerable.Range(0, 3).Select(i => new Screenshot
    {
        CapturedAt = DateTimeOffset.UnixEpoch.AddMinutes(i), ProcessingStatus = ScreenshotStatus.Ready
    }).ToArray();
}
