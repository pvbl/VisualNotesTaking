using System.Windows;

using NSubstitute;

using Shouldly;

using VisualNotes.App.ViewModels;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UiTests;

public sealed class CapturePanelUiTests
{
    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public async Task Region_lock_is_kept_by_the_panel_until_explicitly_unlocked()
    {
        bool? persisted = null;
        var main = CreateMain();
        var panel = new CapturePanelViewModel(main, value =>
        {
            persisted = value;
            return Task.FromResult(true);
        });

        await ((AsyncRelayCommand)panel.ToggleRegionLockCommand).ExecuteAsync();

        persisted.GetValueOrDefault().ShouldBeTrue();
        panel.IsRegionLocked.ShouldBeTrue();
        panel.RegionLockLabel.ShouldBe("Desbloquear región");
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public async Task Section_can_be_created_and_changed_from_capture_panel()
    {
        var main = CreateMain();
        await main.InitializeAsync();
        var session = await main.EnsureActiveSessionAsync();
        var general = session.Sections.Single();
        var panel = new CapturePanelViewModel(main) { NewSectionName = "Ejemplos" };

        await ((AsyncRelayCommand)panel.AddSectionCommand).ExecuteAsync();

        session.Sections.Count.ShouldBe(2);
        panel.Sections.Single(section => section.Title == "Ejemplos").Id.ShouldBe(session.ActiveSectionId!.Value);
        panel.SelectedSectionId.ShouldBe(session.ActiveSectionId);

        panel.SelectedSectionId = general.Id;
        session.ActiveSectionId.ShouldBe(general.Id);
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Panel_defaults_to_the_right_side()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VisualNotes.App", "ViewModels", "CapturePanelViewModel.cs");
        var source = File.ReadAllText(Path.GetFullPath(path));

        source.ShouldContain("_placement = CapturePanelPlacement.Derecha;");
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Capture_uses_the_complete_next_item_draft_and_clears_it_after_success()
    {
        var panel = new CapturePanelViewModel(CreateMain())
        {
            CaptureTitle = "Teorema de Bayes",
            CaptureTags = "examen, probabilidad",
            ContextMarkdown = "Comparar con probabilidad condicionada."
        };
        CaptureDraft? requested = null;
        panel.CaptureRequested += (_, draft) => requested = draft;

        panel.CaptureCommand.Execute(null);

        requested.ShouldBe(new CaptureDraft("Teorema de Bayes", "examen, probabilidad",
            "Comparar con probabilidad condicionada."));
        panel.CaptureCompleted(clearContext: true);
        panel.HasDraft.ShouldBeFalse();
        panel.CaptureTitle.ShouldBeEmpty();
        panel.CaptureTags.ShouldBeEmpty();
        panel.ContextMarkdown.ShouldBeEmpty();
    }

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
    [InlineData(CapturePanelPlacement.Izquierda)]
    [InlineData(CapturePanelPlacement.Derecha)]
    [InlineData(CapturePanelPlacement.Arriba)]
    [InlineData(CapturePanelPlacement.Abajo)]
    public void Docked_panel_touches_the_selected_work_area_edge(CapturePanelPlacement placement)
    {
        var workArea = new Rect(-1920, 0, 1920, 1080);
        var result = CapturePanelViewModel.GetPlacementBounds(placement, workArea, new Rect(-1500, 200, 430, 190));

        if (placement == CapturePanelPlacement.Izquierda) result.Left.ShouldBe(workArea.Left);
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

    [Theory, Trait("Category", "UI"), Trait("Category", "Windows")]
    [InlineData(ContextEditorPlacement.Izquierda)]
    [InlineData(ContextEditorPlacement.Derecha)]
    [InlineData(ContextEditorPlacement.Debajo)]
    public void Detached_context_editor_is_positioned_relative_to_capture_controls(ContextEditorPlacement placement)
    {
        var controls = new Rect(500, 200, 430, 300);
        var result = CapturePanelViewModel.GetContextBounds(
            placement, controls, new Size(380, 225), new Rect(100, 100, 380, 225));

        if (placement == ContextEditorPlacement.Izquierda) result.Right.ShouldBe(controls.Left - 8);
        if (placement == ContextEditorPlacement.Derecha) result.Left.ShouldBe(controls.Right + 8);
        if (placement == ContextEditorPlacement.Debajo) result.Top.ShouldBe(controls.Bottom + 8);
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
        xaml.ShouldContain("Command=\"{Binding SelectPlacementCommand}\"");
        xaml.ShouldContain("CommandParameter=\"Izquierda\"");
        xaml.ShouldContain("CommandParameter=\"Flotante\"");
        xaml.ShouldContain("Key=\"Z\" Modifiers=\"Control\" Command=\"{Binding UndoCommand}\"");
        xaml.ShouldContain("FocusManager.FocusedElement=\"{Binding ElementName=CaptureNowButton, Mode=OneWay}\"");
        xaml.ShouldContain("Text=\"{Binding SessionStatus}\"");
        xaml.ShouldContain("AutomationProperties.AutomationId=\"OpenSessionReviewButton\"");
        xaml.ShouldContain("AutomationProperties.AutomationId=\"PanelCourseSelector\"");
        xaml.ShouldContain("AutomationProperties.AutomationId=\"PanelModuleSelector\"");
        xaml.ShouldContain("AutomationProperties.AutomationId=\"PanelSessionSelector\"");
        xaml.ShouldContain("AutomationProperties.AutomationId=\"CaptureTitleInput\"");
        xaml.ShouldContain("AutomationProperties.AutomationId=\"CaptureTagsInput\"");
        xaml.ShouldContain("AutomationProperties.AutomationId=\"CaptureContextMarkdownInput\"");
        xaml.ShouldContain("AutomationProperties.AutomationId=\"AddTextNoteButton\"");
        xaml.ShouldContain("Text=\"{Binding ContextMarkdown, UpdateSourceTrigger=PropertyChanged}\"");
        xaml.ShouldContain("AutomationProperties.AutomationId=\"PanelAddSectionButton\"");
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Detached_context_window_shares_markdown_and_can_return_inside()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VisualNotes.App", "Views", "ContextPanelWindow.xaml");
        var xaml = File.ReadAllText(Path.GetFullPath(path));

        xaml.ShouldContain("AutomationProperties.AutomationId=\"ContextPanelWindow\"");
        xaml.ShouldContain("Text=\"{Binding ContextMarkdown, UpdateSourceTrigger=PropertyChanged}\"");
        xaml.ShouldContain("SelectedItem=\"{Binding ContextPlacement}\"");
        xaml.ShouldContain("Path=DockInsideCommand");
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
    public void Closing_the_panel_hides_it_and_reactivating_a_session_shows_it_again()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "VisualNotes.App", "App.xaml.cs");
        var source = File.ReadAllText(Path.GetFullPath(path));

        source.ShouldContain("_capturePanel.Closing +=");
        source.ShouldContain("args.Cancel = true;");
        source.ShouldContain("_capturePanel.Hide();");
        source.ShouldContain("_viewModel.SessionActivated += ShowCapturePanel;");
        source.ShouldContain("if (!_capturePanel.IsVisible) _capturePanel.Show();");
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Capturing_from_the_panel_resumes_a_paused_session_first()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "VisualNotes.App", "App.xaml.cs");
        var source = File.ReadAllText(Path.GetFullPath(path));

        var ensure = source.IndexOf("await _viewModel.EnsureActiveSessionAsync();", StringComparison.Ordinal);
        var resume = source.IndexOf("await _viewModel.ResumeActiveSessionAsync();", ensure, StringComparison.Ordinal);
        var capture = source.IndexOf("var frame = await _capture.CaptureAsync(new(captureMode));", StringComparison.Ordinal);
        ensure.ShouldBeGreaterThanOrEqualTo(0);
        resume.ShouldBeGreaterThan(ensure);
        capture.ShouldBeGreaterThan(resume);
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

    private static MainViewModel CreateMain()
    {
        var sessions = Substitute.For<ISessionRepository>();
        sessions.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<NoteSession>>([]));
        return new MainViewModel(new SessionCoordinator(
            sessions,
            Substitute.For<IScreenshotRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IUnitOfWork>()), sessions);
    }
}
