using System.Text;

using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace VisualNotes.UiTests.E2E;

internal sealed class VisualNotesApplication : IDisposable
{
    private readonly string _artifacts = Environment.GetEnvironmentVariable("VISUALNOTES_E2E_ARTIFACTS")
        ?? Path.Combine(AppContext.BaseDirectory, "e2e-artifacts");
    private readonly string _data = Path.Combine(Path.GetTempPath(), "VisualNotes-e2e", Guid.NewGuid().ToString("N"));
    private Application? _application;
    private UIA3Automation? _automation;

    public Window Start()
    {
        Directory.CreateDirectory(_data);
        var executable = Path.Combine(AppContext.BaseDirectory, "VisualNotes.App.exe");
        Environment.SetEnvironmentVariable("VISUALNOTES_DATA_DIRECTORY", _data);
        _application = Application.Launch(executable);
        _automation = new UIA3Automation();
        return Retry.WhileNull(
            () => _application.GetAllTopLevelWindows(_automation)
                .FirstOrDefault(window => window.AutomationId == "MainWindow"),
            TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(250), throwOnTimeout: true).Result!;
    }

    public static AutomationElement ById(AutomationElement root, string id) =>
        Retry.WhileNull(() => root.FindFirstDescendant(cf => cf.ByAutomationId(id)),
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100), throwOnTimeout: true).Result!;

    public void RecordFailure(string testName, Window? window, Exception error)
    {
        var directory = Path.Combine(_artifacts, Sanitize(testName));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "failure.txt"), error.ToString());
        try { Capture.Screen().ToFile(Path.Combine(directory, "desktop.png")); } catch (Exception captureError) { File.AppendAllText(Path.Combine(directory, "failure.txt"), $"{Environment.NewLine}Screenshot: {captureError}"); }
        if (window is not null) File.WriteAllText(Path.Combine(directory, "uia-tree.txt"), DumpTree(window));
        var diagnostics = Path.Combine(_data, "diagnostics");
        if (Directory.Exists(diagnostics))
            foreach (var log in Directory.EnumerateFiles(diagnostics)) File.Copy(log, Path.Combine(directory, Path.GetFileName(log)), true);
    }

    private static string DumpTree(AutomationElement root)
    {
        var output = new StringBuilder();
        Walk(root, output, 0);
        return output.ToString();
    }

    private static void Walk(AutomationElement element, StringBuilder output, int depth)
    {
        output.Append(' ', depth * 2).Append(element.ControlType).Append(" id=").Append(element.AutomationId)
            .Append(" name=").AppendLine(element.Name);
        foreach (var child in element.FindAllChildren()) Walk(child, output, depth + 1);
    }

    private static string Sanitize(string value) => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    public void Dispose()
    {
        _application?.Close();
        _application?.Dispose();
        _automation?.Dispose();
        Environment.SetEnvironmentVariable("VISUALNOTES_DATA_DIRECTORY", null);
        try { if (Directory.Exists(_data)) Directory.Delete(_data, true); } catch (IOException) { }
    }
}
