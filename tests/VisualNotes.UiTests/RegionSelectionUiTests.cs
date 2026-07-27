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

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Selected_region_can_be_moved_resized_locked_and_hidden()
    {
        var selection = new RegionSelectionController();
        selection.SetSelection(new(100, 100, 640, 480));
        selection.MoveBy(25, -10).ShouldBeTrue();
        selection.ResizeBy(160, 120).ShouldBeTrue();
        selection.Selection.ShouldBe(new PhysicalRectangle(125, 90, 800, 600));
        selection.SetLocked(true); selection.MoveBy(10, 10).ShouldBeFalse(); selection.ResizeBy(10, 10).ShouldBeFalse();
        selection.SetHidden(true); selection.IsHidden.ShouldBeTrue();
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Capture_bounds_do_not_include_two_pixel_visual_border()
    {
        var selection = new RegionSelectionController();
        selection.SetSelection(new(50, 60, 800, 450));
        const int visualBorderThickness = 2;
        var captureRequest = new CaptureRequest(ScreenCaptureMode.OneTimeRegion, selection.Selection);
        captureRequest.Region.ShouldBe(new PhysicalRectangle(50, 60, 800, 450));
        captureRequest.Region.Width.ShouldNotBe(800 + visualBorderThickness * 2);
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Selected_region_can_be_deleted_unless_locked()
    {
        var selection = new RegionSelectionController();
        selection.SetSelection(new(10, 10, 20, 20));
        selection.Delete().ShouldBeTrue();
        selection.Selection.ShouldBeNull();
        selection.SetSelection(new(10, 10, 20, 20)); selection.SetLocked(true);
        selection.Delete().ShouldBeFalse();
    }
}
