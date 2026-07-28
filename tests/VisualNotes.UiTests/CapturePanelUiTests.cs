using System.Windows;

using Shouldly;

using VisualNotes.App.ViewModels;

namespace VisualNotes.UiTests;

public sealed class CapturePanelUiTests
{
    [Theory, Trait("Category", "UI"), Trait("Category", "Windows")]
    [InlineData(-1920, 0, 1920, 1080, 1.0)]
    [InlineData(0, -1440, 2560, 1440, 1.25)]
    [InlineData(0, 0, 3840, 2160, 2.0)]
    public void Panel_is_visible_on_monitors_with_different_origins_and_dpi(double x, double y, double width, double height, double dpi)
    {
        var workArea = new Rect(x, y, width / dpi, height / dpi);
        var result = CapturePanelViewModel.ConstrainToWorkArea(new Rect(x - 500, y - 500, 430, 190), workArea);
        workArea.Contains(result.TopLeft).ShouldBeTrue();
        workArea.Contains(result.BottomRight).ShouldBeTrue();
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Panel_xaml_provides_keyboard_shortcuts_and_screen_reader_names()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "VisualNotes.App", "Views", "CapturePanelWindow.xaml");
        var xaml = File.ReadAllText(Path.GetFullPath(path));
        xaml.ShouldContain("KeyBinding");
        xaml.ShouldContain("AutomationProperties.Name");
        xaml.ShouldContain("AutomationProperties.LiveSetting");
        xaml.ShouldContain("AutomationProperties.AutomationId=\"UndoButton\"");
        xaml.ShouldContain("AutomationProperties.AutomationId=\"MarkImportantButton\"");
        xaml.ShouldContain("AutomationProperties.AutomationId=\"AddContextButton\"");
        xaml.ShouldContain("Key=\"Z\" Modifiers=\"Control\" Command=\"{Binding UndoCommand}\"");
        xaml.ShouldContain("FocusManager.FocusedElement=\"{Binding ElementName=CaptureNowButton, Mode=OneWay}\"");
        xaml.ShouldContain("{Binding SessionStatus, Mode=OneWay}");
        xaml.ShouldContain("{Binding CaptureCount, Mode=OneWay}");
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Panel_owner_is_assigned_after_the_main_window_is_shown()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "VisualNotes.App", "App.xaml.cs");
        var source = File.ReadAllText(Path.GetFullPath(path));

        var showMain = source.IndexOf("_window.Show();", StringComparison.Ordinal);
        var assignOwner = source.IndexOf("_capturePanel.Owner = _window;", StringComparison.Ordinal);
        var showPanel = source.IndexOf("_capturePanel.Show();", StringComparison.Ordinal);

        showMain.ShouldBeGreaterThanOrEqualTo(0);
        assignOwner.ShouldBeGreaterThan(showMain);
        showPanel.ShouldBeGreaterThan(assignOwner);
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Global_capture_action_hotkeys_execute_the_same_explicit_commands_as_the_panel()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "VisualNotes.App", "App.xaml.cs");
        var source = File.ReadAllText(Path.GetFullPath(path));
        source.ShouldContain("HotkeyAction.Undo: ExecuteIfAvailable(_viewModel.UndoCommand)");
        source.ShouldContain("HotkeyAction.MarkImportant: ExecuteIfAvailable(_viewModel.MarkImportantCommand)");
        source.ShouldContain("HotkeyAction.AddContext: ExecuteIfAvailable(_viewModel.AddContextCommand)");
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void A_second_instance_activates_the_running_window_before_registering_hotkeys()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "VisualNotes.App", "App.xaml.cs");
        var source = File.ReadAllText(Path.GetFullPath(path));

        var acquireInstance = source.IndexOf("new Mutex(true, SingleInstanceMutexName", StringComparison.Ordinal);
        var activateExisting = source.IndexOf("NativeWindow.ActivateRunningInstance();", StringComparison.Ordinal);
        var createHotkeys = source.IndexOf("new GlobalHotkeyService", StringComparison.Ordinal);

        acquireInstance.ShouldBeGreaterThanOrEqualTo(0);
        activateExisting.ShouldBeGreaterThan(acquireInstance);
        createHotkeys.ShouldBeGreaterThan(activateExisting);
    }
}
