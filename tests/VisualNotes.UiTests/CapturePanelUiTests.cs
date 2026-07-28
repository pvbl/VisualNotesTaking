using Shouldly;
using System.Windows;
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
}
