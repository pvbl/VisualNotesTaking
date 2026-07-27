using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

public enum RegionSelectionState { Selecting, Selected, Confirmed, Cancelled }

/// <summary>State machine shared by the selection overlay and UI automation tests.</summary>
public sealed class RegionSelectionController
{
    private (int X, int Y)? _origin;
    public RegionSelectionState State { get; private set; } = RegionSelectionState.Selecting;
    public PhysicalRectangle? Selection { get; private set; }

    public void Begin(int physicalX, int physicalY) { _origin = (physicalX, physicalY); Selection = null; State = RegionSelectionState.Selecting; }
    public void Update(int physicalX, int physicalY)
    {
        if (_origin is not { } origin || State is RegionSelectionState.Cancelled or RegionSelectionState.Confirmed) return;
        Selection = ScreenCaptureGeometry.Normalize(origin.X, origin.Y, physicalX, physicalY);
        State = Selection.IsEmpty ? RegionSelectionState.Selecting : RegionSelectionState.Selected;
    }
    public bool Confirm()
    {
        if (Selection is null || Selection.IsEmpty) return false;
        State = RegionSelectionState.Confirmed; return true;
    }
    public void Cancel() => State = RegionSelectionState.Cancelled;
    public void Reset() { _origin = null; Selection = null; State = RegionSelectionState.Selecting; }
}
