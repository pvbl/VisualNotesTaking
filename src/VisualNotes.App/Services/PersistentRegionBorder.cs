using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using Brushes = System.Windows.Media.Brushes;

namespace VisualNotes.App.Services;

/// <summary>A click-through visual guide. Capture hides all app windows before copying pixels.</summary>
public sealed class PersistentRegionBorder : IDisposable
{
    private readonly Window _window = new()
    {
        AllowsTransparency = true, Background = Brushes.Transparent, BorderBrush = Brushes.DeepSkyBlue,
        BorderThickness = new Thickness(2), WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false, Topmost = true, IsHitTestVisible = false, Focusable = false
    };

    public PersistentRegionBorder() => _window.SourceInitialized += (_, _) =>
    {
        var handle = new WindowInteropHelper(_window).Handle;
        SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x20 | 0x08000000);
    };

    public void Show(PersistentCaptureRegion region)
    {
        if (region.IsHidden || !region.ShowNonCapturableBorder) { Hide(); return; }
        var monitor = WindowsScreenCaptureService.GetMonitors().FirstOrDefault(x => x.DeviceName == region.MonitorDeviceName);
        var dpiX = monitor?.DpiX ?? region.DpiX; var dpiY = monitor?.DpiY ?? region.DpiY;
        _window.Left = ScreenCaptureGeometry.PhysicalToDip(region.Bounds.X, dpiX);
        _window.Top = ScreenCaptureGeometry.PhysicalToDip(region.Bounds.Y, dpiY);
        _window.Width = ScreenCaptureGeometry.PhysicalToDip(region.Bounds.Width, dpiX);
        _window.Height = ScreenCaptureGeometry.PhysicalToDip(region.Bounds.Height, dpiY);
        _window.Show();
    }

    public void Hide() => _window.Hide();
    public void Dispose() => _window.Close();
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint window, int index, int value);
}
