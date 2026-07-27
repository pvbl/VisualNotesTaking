using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

public enum RegionSelectionState { Selecting, Selected, Confirmed, Cancelled }

/// <summary>State machine shared by the selection overlay and UI automation tests.</summary>
public sealed class RegionSelectionController
{
    private (int X, int Y)? _origin;
    public RegionSelectionState State { get; private set; } = RegionSelectionState.Selecting;
    public PhysicalRectangle? Selection { get; private set; }
    public bool IsLocked { get; private set; }
    public bool IsHidden { get; private set; }

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
    public bool MoveBy(int deltaX, int deltaY)
    {
        if (IsLocked || Selection is not { } value) return false;
        Selection = value with { X = checked(value.X + deltaX), Y = checked(value.Y + deltaY) }; return true;
    }
    public bool ResizeBy(int deltaWidth, int deltaHeight, int minimumSize = 8)
    {
        if (IsLocked || Selection is not { } value) return false;
        var width = Math.Max(minimumSize, checked(value.Width + deltaWidth));
        var height = Math.Max(minimumSize, checked(value.Height + deltaHeight));
        Selection = value with { Width = width, Height = height }; return true;
    }
    public void SetSelection(PhysicalRectangle value)
    {
        if (!ScreenCaptureGeometry.IsValid(value)) throw new ArgumentOutOfRangeException(nameof(value));
        Selection = value; State = RegionSelectionState.Selected;
    }
    public bool Delete()
    {
        if (IsLocked || Selection is null) return false;
        _origin = null; Selection = null; State = RegionSelectionState.Selecting; return true;
    }
    public void SetLocked(bool value) => IsLocked = value;
    public void SetHidden(bool value) => IsHidden = value;
    public void Reset() { _origin = null; Selection = null; IsLocked = false; IsHidden = false; State = RegionSelectionState.Selecting; }
}
