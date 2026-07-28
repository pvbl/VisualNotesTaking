using Shouldly;

using VisualNotes.App.ViewModels;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UiTests;

public sealed class HotkeySettingsUiTests
{
    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Changing_settings_displays_duplicate_conflict_message()
    {
        using var service = new GlobalHotkeyService(new FakeAdapter());
        var viewModel = new SettingsViewModel(service);
        var captureGesture = viewModel.Bindings.Single(x => x.Action == HotkeyAction.CaptureRegion).Gesture;
        viewModel.ReplaceBinding(HotkeyAction.TogglePause, captureGesture);

        viewModel.SaveCommand.Execute(null);

        viewModel.HasConflict.ShouldBeTrue();
        viewModel.ConflictMessage.ShouldContain("asignado más de una vez");
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Correcting_settings_clears_previous_conflict_message()
    {
        using var service = new GlobalHotkeyService(new FakeAdapter());
        var viewModel = new SettingsViewModel(service);
        var captureGesture = viewModel.Bindings.Single(x => x.Action == HotkeyAction.CaptureRegion).Gesture;
        viewModel.ReplaceBinding(HotkeyAction.TogglePause, captureGesture);
        viewModel.SaveCommand.Execute(null);
        viewModel.ReplaceBinding(HotkeyAction.TogglePause, new(HotkeyModifiers.Control, 0x70));

        viewModel.SaveCommand.Execute(null);

        viewModel.HasConflict.ShouldBeFalse();
        viewModel.ConflictMessage.ShouldBeEmpty();
    }

    private sealed class FakeAdapter : IGlobalHotkeyPlatformAdapter
    {
        public event EventHandler<int>? HotkeyPressed { add { } remove { } }
        public bool Register(int id, HotkeyGesture gesture) => true;
        public void Unregister(int id) { }
        public void Dispose() { }
    }
}
