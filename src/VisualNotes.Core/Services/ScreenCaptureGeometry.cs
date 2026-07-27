using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

/// <summary>Converts monitor-local WPF device-independent units to virtual-desktop physical pixels.</summary>
public static class ScreenCaptureGeometry
{
    public const double DefaultDpi = 96d;

    public static int DipToPhysical(double dip, uint dpi) =>
        checked((int)Math.Round(dip * dpi / DefaultDpi, MidpointRounding.AwayFromZero));

    public static double PhysicalToDip(int pixels, uint dpi) =>
        pixels * DefaultDpi / ValidateDpi(dpi);

    public static PhysicalRectangle DipToPhysical(
        double x, double y, double width, double height, MonitorCaptureInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var left = monitor.Bounds.X + DipToPhysical(x, monitor.DpiX);
        var top = monitor.Bounds.Y + DipToPhysical(y, monitor.DpiY);
        var right = monitor.Bounds.X + DipToPhysical(x + width, monitor.DpiX);
        var bottom = monitor.Bounds.Y + DipToPhysical(y + height, monitor.DpiY);
        return Normalize(left, top, right, bottom);
    }

    public static PhysicalRectangle Normalize(int x1, int y1, int x2, int y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    public static PhysicalRectangle Intersect(PhysicalRectangle value, PhysicalRectangle bounds)
    {
        var left = Math.Max(value.X, bounds.X);
        var top = Math.Max(value.Y, bounds.Y);
        var right = Math.Min(value.Right, bounds.Right);
        var bottom = Math.Min(value.Bottom, bounds.Bottom);
        return new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static uint ValidateDpi(uint dpi) => dpi == 0 ? throw new ArgumentOutOfRangeException(nameof(dpi)) : dpi;
}
