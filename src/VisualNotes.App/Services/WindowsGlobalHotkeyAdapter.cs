using System.Runtime.InteropServices;
using System.Windows.Interop;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.App.Services;

public sealed class WindowsGlobalHotkeyAdapter : IGlobalHotkeyPlatformAdapter
{
    private const int WmHotkey = 0x0312;
    private readonly HwndSource _messageWindow;
    private bool _disposed;

    public WindowsGlobalHotkeyAdapter()
    {
        _messageWindow = new HwndSource(new HwndSourceParameters("VisualNotes.GlobalHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        });
        _messageWindow.AddHook(WindowProc);
    }

    public event EventHandler<int>? HotkeyPressed;

    public bool Register(int id, HotkeyGesture gesture) => RegisterHotKey(
        _messageWindow.Handle, id, (uint)gesture.Modifiers | 0x4000, gesture.VirtualKey);

    public void Unregister(int id) => UnregisterHotKey(_messageWindow.Handle, id);

    public void Dispose()
    {
        if (_disposed) return;
        _messageWindow.RemoveHook(WindowProc);
        _messageWindow.Dispose();
        _disposed = true;
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, wParam.ToInt32());
        }
        return 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);
}
