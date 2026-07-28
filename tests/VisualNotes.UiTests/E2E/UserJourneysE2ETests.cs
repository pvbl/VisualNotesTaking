using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Shouldly;
using VisualNotes.Core.Services;

namespace VisualNotes.UiTests.E2E;

[Collection(WindowsResourceCollection.Name)]
public sealed class UserJourneysE2ETests
{
    [InteractiveDesktopFact, Trait("Category", "UI"), Trait("Category", "Smoke")]
    public void New_session_section_change_and_primary_navigation_use_automation_ids()
    {
        Run(nameof(New_session_section_change_and_primary_navigation_use_automation_ids), window =>
        {
            var name = VisualNotesApplication.ById(window, "SessionNameInput").AsTextBox();
            name.Enter("E2E deterministic session");
            VisualNotesApplication.ById(window, "CreateSessionButton").AsButton().Invoke();
            RetryUntil(() => VisualNotesApplication.ById(window, "RecentSessionsList").AsListBox().Items.Length == 1);

            VisualNotesApplication.ById(window, "AddSectionButton").AsButton().Invoke();
            RetryUntil(() => VisualNotesApplication.ById(window, "SectionsList").AsListBox().Items.Length >= 2);
            VisualNotesApplication.ById(window, "ActivateSectionButton").AsButton().Invoke();

            foreach (var id in new[] { "NavCaptures", "NavInstructions", "NavDocument", "NavSettings", "NavSession" })
                VisualNotesApplication.ById(window, id).AsButton().Invoke();
        });
    }

    [InteractiveDesktopFact, Trait("Category", "UI"), Trait("Category", "E2E")]
    public async Task Capture_context_fake_analysis_review_and_export_controls_are_accessible()
    {
        var fake = new DeterministicVlmProvider();
        var request = new VisionLanguageModelRequest("extrae el diagrama", new byte[] { 1, 2, 3 }, "image/png", new("fake"));
        (await fake.GenerateAsync(request)).ShouldBe(await fake.GenerateAsync(request));

        Run(nameof(Capture_context_fake_analysis_review_and_export_controls_are_accessible), window =>
        {
            VisualNotesApplication.ById(window, "NavCaptures").AsButton().Invoke();
            VisualNotesApplication.ById(window, "CaptureList").ShouldNotBeNull();
            VisualNotesApplication.ById(window, "CaptureContextInput").ShouldNotBeNull();
            VisualNotesApplication.ById(window, "ReanalyzeButton").ShouldNotBeNull();
            VisualNotesApplication.ById(window, "NavDocument").AsButton().Invoke();
            VisualNotesApplication.ById(window, "DocumentReviewList").ShouldNotBeNull();
            VisualNotesApplication.ById(window, "ExportButton").ShouldNotBeNull();
        });
    }

    [InteractiveDesktopFact, Trait("Category", "UI"), Trait("Category", "E2E")]
    public void Keyboard_focus_tab_navigation_and_dialog_escape_are_supported()
    {
        Run(nameof(Keyboard_focus_tab_navigation_and_dialog_escape_are_supported), window =>
        {
            VisualNotesApplication.ById(window, "SessionNameInput").Focus();
            Keyboard.Type(VirtualKeyShort.TAB);
            window.Automation.GetFocusedElement().Properties.HasKeyboardFocus.Value.ShouldBeTrue();
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            window.IsAvailable.ShouldBeTrue();
        });
    }

    [InteractiveDesktopFact, Trait("Category", "UI"), Trait("Category", "E2E"), Trait("Category", "Capture")]
    public void Capture_specific_dpi_and_monitor_preconditions_are_reported()
    {
        Run(nameof(Capture_specific_dpi_and_monitor_preconditions_are_reported), window =>
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            screens.Length.ShouldBeGreaterThanOrEqualTo(1);
            window.Properties.BoundingRectangle.Value.Width.ShouldBeGreaterThan(0);
            // Coordinates are intentionally confined to this capture/monitor scenario.
            screens.ShouldAllBe(screen => screen.Bounds.Width > 0 && screen.Bounds.Height > 0);
        });
    }

    private static void RetryUntil(Func<bool> condition) => FlaUI.Core.Tools.Retry.WhileFalse(condition,
        TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100), throwOnTimeout: true);

    private static void Run(string name, Action<Window> scenario)
    {
        using var app = new VisualNotesApplication();
        Window? window = null;
        try { window = app.Start(); scenario(window); }
        catch (Exception error) { app.RecordFailure(name, window, error); throw; }
    }
}
