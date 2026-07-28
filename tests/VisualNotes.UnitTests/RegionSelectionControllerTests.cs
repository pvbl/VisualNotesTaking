using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

[Trait("Category", "Unit")]
public sealed class RegionSelectionControllerTests
{
    [Fact]
    public void Dragging_normalizes_both_directions_and_requires_a_non_empty_region()
    {
        var controller = new RegionSelectionController();

        controller.Update(20, 20);
        controller.Selection.ShouldBeNull();
        controller.Confirm().ShouldBeFalse();

        controller.Begin(20, 20);
        controller.Update(20, 20);
        controller.Selection.ShouldBe(new PhysicalRectangle(20, 20, 0, 0));
        controller.State.ShouldBe(RegionSelectionState.Selecting);
        controller.Confirm().ShouldBeFalse();

        controller.Update(5, 8);
        controller.Selection.ShouldBe(new PhysicalRectangle(5, 8, 15, 12));
        controller.State.ShouldBe(RegionSelectionState.Selected);
        controller.Confirm().ShouldBeTrue();
        controller.State.ShouldBe(RegionSelectionState.Confirmed);

        controller.Update(100, 100);
        controller.Selection.ShouldBe(new PhysicalRectangle(5, 8, 15, 12));
    }

    [Fact]
    public void Cancellation_stops_an_active_drag()
    {
        var controller = new RegionSelectionController();
        controller.Begin(0, 0);
        controller.Cancel();

        controller.Update(10, 10);

        controller.State.ShouldBe(RegionSelectionState.Cancelled);
        controller.Selection.ShouldBeNull();
    }

    [Fact]
    public void Existing_selection_can_move_resize_and_enforce_the_minimum_size()
    {
        var controller = new RegionSelectionController();
        controller.MoveBy(1, 1).ShouldBeFalse();
        controller.ResizeBy(1, 1).ShouldBeFalse();

        controller.SetSelection(new(10, 20, 30, 40));
        controller.MoveBy(-5, 6).ShouldBeTrue();
        controller.ResizeBy(-100, -100).ShouldBeTrue();

        controller.Selection.ShouldBe(new PhysicalRectangle(5, 26, 8, 8));
        controller.State.ShouldBe(RegionSelectionState.Selected);
    }

    [Fact]
    public void Lock_prevents_move_resize_and_delete_until_released()
    {
        var controller = new RegionSelectionController();
        controller.SetSelection(new(10, 20, 30, 40));
        controller.SetLocked(true);

        controller.MoveBy(1, 1).ShouldBeFalse();
        controller.ResizeBy(1, 1).ShouldBeFalse();
        controller.Delete().ShouldBeFalse();
        controller.Selection.ShouldBe(new PhysicalRectangle(10, 20, 30, 40));

        controller.SetLocked(false);
        controller.Delete().ShouldBeTrue();
        controller.Selection.ShouldBeNull();
        controller.State.ShouldBe(RegionSelectionState.Selecting);
        controller.Delete().ShouldBeFalse();
    }

    [Fact]
    public void Lock_prevents_a_new_drag_from_replacing_the_selection()
    {
        var controller = new RegionSelectionController();
        controller.SetSelection(new(100, 120, 640, 480));
        controller.SetLocked(true);

        controller.Begin(10, 20);
        controller.Update(30, 40);

        controller.Selection.ShouldBe(new PhysicalRectangle(100, 120, 640, 480));
        controller.State.ShouldBe(RegionSelectionState.Selected);
    }

    [Fact]
    public void Invalid_explicit_selection_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new RegionSelectionController().SetSelection(new(0, 0, 0, 10)));
    }

    [Fact]
    public void Reset_clears_selection_lock_visibility_and_terminal_state()
    {
        var controller = new RegionSelectionController();
        controller.SetSelection(new(1, 2, 10, 20));
        controller.SetLocked(true);
        controller.SetHidden(true);
        controller.Cancel();

        controller.Reset();

        controller.Selection.ShouldBeNull();
        controller.IsLocked.ShouldBeFalse();
        controller.IsHidden.ShouldBeFalse();
        controller.State.ShouldBe(RegionSelectionState.Selecting);
    }
}
