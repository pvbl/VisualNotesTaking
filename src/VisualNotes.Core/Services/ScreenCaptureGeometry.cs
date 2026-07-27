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

    public static bool IsValid(PhysicalRectangle value, int minimumSize = 8) =>
        value.Width >= minimumSize && value.Height >= minimumSize &&
        (long)value.X + value.Width <= int.MaxValue && (long)value.Y + value.Height <= int.MaxValue;

    /// <summary>Keeps the size when possible and clamps the rectangle completely inside the monitor.</summary>
    public static PhysicalRectangle ClampTo(PhysicalRectangle value, PhysicalRectangle bounds)
    {
        if (bounds.IsEmpty || value.IsEmpty) return new(bounds.X, bounds.Y, 0, 0);
        var width = Math.Min(value.Width, bounds.Width);
        var height = Math.Min(value.Height, bounds.Height);
        var x = Math.Clamp(value.X, bounds.X, bounds.Right - width);
        var y = Math.Clamp(value.Y, bounds.Y, bounds.Bottom - height);
        return new(x, y, width, height);
    }

    private static uint ValidateDpi(uint dpi) => dpi == 0 ? throw new ArgumentOutOfRangeException(nameof(dpi)) : dpi;
}
