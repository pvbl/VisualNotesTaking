using System.Xml.Linq;

using Shouldly;

namespace VisualNotes.UiTests;

public sealed class AccessibilityUiTests
{
    private static readonly string AppDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "VisualNotes.App"));

    [Fact, Trait("Category", "UI"), Trait("Category", "Accessibility")]
    public void Critical_controls_expose_stable_automation_ids_and_accessible_names()
    {
        var requiredIds = new[] { "MainWindow", "SessionNameInput", "CreateSessionButton", "CaptureList",
            "CaptureContextInput", "DocumentReviewList", "ExportButton", "CapturePanelWindow", "CaptureNowButton" };
        var elements = Directory.EnumerateFiles(AppDirectory, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path).Root!.DescendantsAndSelf()).ToArray();

        foreach (var id in requiredIds)
        {
            var element = elements.SingleOrDefault(candidate => candidate.Attributes()
                .Any(attribute => attribute.Name.LocalName == "AutomationProperties.AutomationId" && attribute.Value == id));
            element.ShouldNotBeNull($"AutomationId '{id}' must remain available to UI Automation");
            var hasName = element.Attributes().Any(attribute => attribute.Name.LocalName == "AutomationProperties.Name") ||
                          element.Attribute("Content") is not null;
            hasName.ShouldBeTrue($"'{id}' must expose a name, directly or through its button content");
        }
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Accessibility")]
    public void Application_defines_scalable_controls_and_a_non_colour_focus_indicator()
    {
        var xaml = File.ReadAllText(Path.Combine(AppDirectory, "App.xaml"));
        xaml.ShouldContain("FontSize\" Value=\"14");
        xaml.ShouldContain("MinHeight\" Value=\"32");
        xaml.ShouldContain("FocusVisualStyle");
        xaml.ShouldContain("BorderThickness=\"3\"");
    }
}
