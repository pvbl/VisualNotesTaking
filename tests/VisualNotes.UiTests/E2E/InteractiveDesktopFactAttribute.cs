namespace VisualNotes.UiTests.E2E;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class InteractiveDesktopFactAttribute : FactAttribute
{
    public InteractiveDesktopFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || !string.Equals(
                Environment.GetEnvironmentVariable("VISUALNOTES_INTERACTIVE_UI"), "1", StringComparison.Ordinal))
            Skip = "Requires an unlocked interactive Windows desktop (VISUALNOTES_INTERACTIVE_UI=1).";
    }
}
