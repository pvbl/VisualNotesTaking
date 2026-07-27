using System.Diagnostics.CodeAnalysis;

// WPF owns these lifecycle/event signatures, so Task-returning methods cannot be
// substituted. Both suppressions target one member rather than a type or namespace.
[assembly: SuppressMessage(
    "Major Code Smell",
    "S3168:Return Task instead",
    Justification = "WPF Application.OnStartup fixes the override signature to void.",
    Scope = "member",
    Target = "~M:VisualNotes.App.App.OnStartup(System.Windows.StartupEventArgs)")]
[assembly: SuppressMessage(
    "Major Code Smell",
    "S3168:Return Task instead",
    Justification = "This method is the WPF tray-menu click event handler boundary.",
    Scope = "member",
    Target = "~M:VisualNotes.App.App.ExitApplication")]
