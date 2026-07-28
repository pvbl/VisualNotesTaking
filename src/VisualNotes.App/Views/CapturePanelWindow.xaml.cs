using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VisualNotes.App.Views;

public partial class CapturePanelWindow : Window
{
    private const uint WdaExcludeFromCapture = 0x11;

    public CapturePanelWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => _ = SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, WdaExcludeFromCapture);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
}
