using Shouldly;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UiTests;

public sealed class RegionSelectionUiTests
{
    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Selection_can_be_confirmed_and_reset()
    {
        var selection = new RegionSelectionController();
        selection.Begin(-900, 30); selection.Update(-100, 500);
        selection.Confirm().ShouldBeTrue(); selection.State.ShouldBe(RegionSelectionState.Confirmed);
        selection.Reset(); selection.Selection.ShouldBeNull(); selection.State.ShouldBe(RegionSelectionState.Selecting);
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Selection_can_be_cancelled() { var selection = new RegionSelectionController(); selection.Cancel(); selection.State.ShouldBe(RegionSelectionState.Cancelled); }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Drag_can_cross_to_monitor_with_negative_origin()
    {
        var selection = new RegionSelectionController(); selection.Begin(200, 100); selection.Update(-1200, 700);
        selection.Selection.ShouldBe(new PhysicalRectangle(-1200, 100, 1400, 600));
    }
}
