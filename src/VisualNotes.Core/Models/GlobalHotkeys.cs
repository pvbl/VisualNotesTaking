namespace VisualNotes.Core.Models;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

public enum HotkeyAction
{
    CaptureFullDesktop,
    CaptureCurrentMonitor,
    CaptureActiveWindow,
    CaptureRegion,
    TogglePause,
    NextSection,
    PreviousSection,
    Undo,
    MarkImportant,
    AddContext
}

public sealed record HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public override string ToString() => $"{Modifiers}+0x{VirtualKey:X2}";
}

public sealed record HotkeyBinding(HotkeyAction Action, HotkeyGesture Gesture, bool IsEnabled = true);

public sealed record HotkeyConflict(HotkeyAction Action, string Message);

public sealed record HotkeyConfigurationResult(bool Succeeded, IReadOnlyList<HotkeyConflict> Conflicts)
{
    public static HotkeyConfigurationResult Success { get; } = new(true, []);
}
