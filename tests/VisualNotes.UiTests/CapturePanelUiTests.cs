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

    [Theory, Trait("Category", "UI"), Trait("Category", "Windows")]
    [InlineData(CapturePanelPlacement.Derecha)]
    [InlineData(CapturePanelPlacement.Arriba)]
    [InlineData(CapturePanelPlacement.Abajo)]
    public void Docked_panel_touches_the_selected_work_area_edge(CapturePanelPlacement placement)
    {
        var workArea = new Rect(-1920, 0, 1920, 1080);
        var result = CapturePanelViewModel.GetPlacementBounds(placement, workArea, new Rect(-1500, 200, 430, 190));

        if (placement == CapturePanelPlacement.Derecha) result.Right.ShouldBe(workArea.Right);
        if (placement == CapturePanelPlacement.Arriba) result.Top.ShouldBe(workArea.Top);
        if (placement == CapturePanelPlacement.Abajo) result.Bottom.ShouldBe(workArea.Bottom);
        workArea.Contains(result.TopLeft).ShouldBeTrue();
        workArea.Contains(result.BottomRight).ShouldBeTrue();
    }

    [Theory, Trait("Category", "UI"), Trait("Category", "Windows")]
    [InlineData(1366, 228)]
    [InlineData(1920, 320)]
    [InlineData(2560, 427)]
    [InlineData(3840, 480)]
    public void Right_panel_uses_about_one_sixth_of_the_screen_without_becoming_unusable(
        double screenWidth, double expectedWidth)
    {
        var workArea = new Rect(0, 0, screenWidth, 1080);
        var result = CapturePanelViewModel.GetPlacementBounds(
            CapturePanelPlacement.Derecha, workArea, new Rect(100, 100, 430, 190));

        result.Width.ShouldBe(expectedWidth, 0.001);
        result.Height.ShouldBe(workArea.Height);
        result.Top.ShouldBe(workArea.Top);
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
        xaml.ShouldContain("AutomationProperties.AutomationId=\"CapturePanelPlacementSelector\"");
        xaml.ShouldContain("ItemsSource=\"{Binding Placements}\"");
        xaml.ShouldContain("Key=\"Z\" Modifiers=\"Control\" Command=\"{Binding UndoCommand}\"");
        xaml.ShouldContain("FocusManager.FocusedElement=\"{Binding ElementName=CaptureNowButton, Mode=OneWay}\"");
        xaml.ShouldContain("{Binding SessionStatus, Mode=OneWay}");
        xaml.ShouldContain("{Binding CaptureCount, Mode=OneWay}");
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Panel_is_not_owned_by_the_main_window_so_it_remains_visible_when_main_is_minimized()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "VisualNotes.App", "App.xaml.cs");
        var source = File.ReadAllText(Path.GetFullPath(path));

        var showMain = source.IndexOf("_window.Show();", StringComparison.Ordinal);
        var showPanel = source.IndexOf("_capturePanel.Show();", StringComparison.Ordinal);

        showMain.ShouldBeGreaterThanOrEqualTo(0);
        showPanel.ShouldBeGreaterThan(showMain);
        source.ShouldNotContain("_capturePanel.Owner = _window;");
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
