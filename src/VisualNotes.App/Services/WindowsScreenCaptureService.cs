using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using Forms = System.Windows.Forms;

namespace VisualNotes.App.Services;

public interface IRegionSelectionOverlay
{
    Task<PhysicalRectangle?> SelectAsync(CancellationToken cancellationToken);
}

/// <summary>Captures physical desktop pixels after all VisualNotes surfaces have been hidden.</summary>
public sealed class WindowsScreenCaptureService(IRegionSelectionOverlay overlay) : IScreenCaptureService
{
    public static IReadOnlyList<MonitorCaptureInfo> GetMonitors() => Forms.Screen.AllScreens.Select(GetMonitor).ToArray();

    public async Task<CapturedFrame?> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var target = await ResolveTargetAsync(request, cancellationToken).ConfigureAwait(true);
        if (target is null) return null;

        var visibleApplicationWindows = Application.Current.Windows.Cast<Window>().Where(x => x.IsVisible).ToArray();
        foreach (var window in visibleApplicationWindows) window.Hide();
        // The selector is closed before this point. Yield once so DWM can compose a frame without
        // the overlay, persistent border, or floating panel before CopyFromScreen executes.
        try
        {
            await Task.Delay(50, cancellationToken).ConfigureAwait(true);
            var capturedAt = DateTimeOffset.UtcNow;
            var bytes = await Task.Run(() => EncodePng(target.Bounds), cancellationToken).ConfigureAwait(false);
            var metadata = new CaptureMetadata(request.Mode, target.Bounds, capturedAt, target.Monitor?.DeviceName,
                target.Window, target.WindowTitle, target.Monitor?.DpiX ?? 96, target.Monitor?.DpiY ?? 96,
                target.Bounds.Width, target.Bounds.Height);
            return new CapturedFrame(Guid.NewGuid(), capturedAt,
                new CaptureRegion(target.Bounds.X, target.Bounds.Y, target.Bounds.Width, target.Bounds.Height),
                bytes, "image/png", metadata);
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var window in visibleApplicationWindows) window.Show();
            });
        }
    }

    private async Task<CaptureTarget?> ResolveTargetAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        if (request.Mode == ScreenCaptureMode.OneTimeRegion)
        {
            var selected = request.Region ?? await overlay.SelectAsync(cancellationToken).ConfigureAwait(true);
            return selected is null || selected.IsEmpty ? null : new(selected, FindMonitor(selected), null, null);
        }

        if (request.Mode == ScreenCaptureMode.FullVirtualDesktop)
        {
            var area = Forms.SystemInformation.VirtualScreen;
            return new(new(area.X, area.Y, area.Width, area.Height), null, null, null);
        }

        if (request.Mode == ScreenCaptureMode.ActiveWindow)
        {
            var window = request.WindowHandle ?? NativeMethods.GetForegroundWindow();
            if (window == 0 || !NativeMethods.GetWindowRect(window, out var rectangle)) return null;
            var bounds = new PhysicalRectangle(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
            return new(bounds, FindMonitor(bounds), window, GetWindowTitle(window));
        }

        var screen = request.MonitorDeviceName is null
            ? Forms.Screen.FromPoint(Forms.Cursor.Position)
            : Forms.Screen.AllScreens.FirstOrDefault(x => x.DeviceName == request.MonitorDeviceName) ?? Forms.Screen.PrimaryScreen!;
        var monitor = GetMonitor(screen);
        return new(monitor.Bounds, monitor, null, null);
    }

    private static byte[] EncodePng(PhysicalRectangle bounds)
    {
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Size(bounds.Width, bounds.Height), CopyPixelOperation.SourceCopy);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static MonitorCaptureInfo? FindMonitor(PhysicalRectangle bounds) => Forms.Screen.AllScreens
        .Select(GetMonitor).OrderByDescending(x => ScreenCaptureGeometry.Intersect(x.Bounds, bounds).Width *
            ScreenCaptureGeometry.Intersect(x.Bounds, bounds).Height).FirstOrDefault();

    private static MonitorCaptureInfo GetMonitor(Forms.Screen screen)
    {
        var point = new NativeMethods.Point(screen.Bounds.Left, screen.Bounds.Top);
        var handle = NativeMethods.MonitorFromPoint(point, 2);
        _ = NativeMethods.GetDpiForMonitor(handle, 0, out var dpiX, out var dpiY);
        return new(screen.DeviceName, new(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height),
            dpiX == 0 ? 96u : dpiX, dpiY == 0 ? 96u : dpiY, screen.Primary);
    }

    private static string? GetWindowTitle(nint handle)
    {
        var length = NativeMethods.GetWindowTextLength(handle);
        if (length == 0) return null;
        var value = new StringBuilder(length + 1);
        _ = NativeMethods.GetWindowText(handle, value, value.Capacity);
        return value.ToString();
    }

    private sealed record CaptureTarget(PhysicalRectangle Bounds, MonitorCaptureInfo? Monitor, nint? Window, string? WindowTitle);

    private static partial class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct Point(int x, int y)
        {
            internal readonly int X = x;
            internal readonly int Y = y;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        [LibraryImport("user32.dll")] internal static partial nint GetForegroundWindow();
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool GetWindowRect(nint window, out Rect rectangle);
        [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW", StringMarshalling = StringMarshalling.Utf16)] internal static partial int GetWindowTextLength(nint window);
        [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)] internal static partial int GetWindowText(nint window, StringBuilder text, int count);
        [LibraryImport("user32.dll")] internal static partial nint MonitorFromPoint(Point point, uint flags);
        [LibraryImport("Shcore.dll")] internal static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    }
}
