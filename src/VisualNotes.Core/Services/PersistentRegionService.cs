using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

/// <summary>Persists regions and reconciles them with the monitor topology at startup.</summary>
public sealed class PersistentRegionService(ISettingsStore settings)
{
    public const string ActiveRegionKey = "capture.region.active";
    public const string FavoritesKey = "capture.region.favorites";

    public Task SaveAsync(PersistentCaptureRegion region, CancellationToken cancellationToken = default)
    {
        Validate(region);
        return settings.SetAsync(ActiveRegionKey, region, cancellationToken);
    }

    public async Task<RegionRestoreResult> RestoreAsync(
        IReadOnlyCollection<MonitorCaptureInfo> monitors,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var saved = await settings.GetAsync<PersistentCaptureRegion>(ActiveRegionKey, cancellationToken);
        if (saved is null) return new(null, RegionRestoreStatus.Invalid, "No hay una región guardada.");
        if (!ScreenCaptureGeometry.IsValid(saved.Bounds) || saved.DpiX == 0 || saved.DpiY == 0)
            return new(null, RegionRestoreStatus.Invalid, "Las coordenadas o DPI guardados no son válidos.");
        if (monitors.Count == 0) return new(null, RegionRestoreStatus.Invalid, "No hay monitores disponibles.");

        var monitor = monitors.FirstOrDefault(x => x.DeviceName == saved.MonitorDeviceName);
        var status = RegionRestoreStatus.Restored;
        string? warning = null;
        if (monitor is null)
        {
            monitor = monitors.FirstOrDefault(x => x.IsPrimary) ?? monitors.First();
            status = RegionRestoreStatus.MovedToAvailableMonitor;
            warning = $"El monitor {saved.MonitorDeviceName} está desconectado; se movió la región a {monitor.DeviceName}.";
        }
        else if (monitor.Bounds != saved.SavedMonitorBounds)
        {
            status = RegionRestoreStatus.AdjustedForResolution;
            warning = "La resolución del monitor cambió; se ajustó la región al área visible.";
        }

        var bounds = ScreenCaptureGeometry.ClampTo(saved.Bounds, monitor.Bounds);
        if (bounds != saved.Bounds && status == RegionRestoreStatus.Restored)
        {
            status = RegionRestoreStatus.AdjustedForResolution;
            warning = "La región estaba fuera de pantalla y se ajustó al área visible.";
        }
        var restored = saved with
        {
            Bounds = bounds,
            MonitorDeviceName = monitor.DeviceName,
            DpiX = monitor.DpiX,
            DpiY = monitor.DpiY,
            SessionId = sessionId,
            SavedMonitorBounds = monitor.Bounds
        };
        return new(restored, status, warning);
    }

    public async Task SaveFavoriteAsync(string name, PersistentCaptureRegion region, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("El favorito debe tener nombre.", nameof(name));
        Validate(region);
        var favorites = (await settings.GetAsync<List<FavoriteCaptureRegion>>(FavoritesKey, cancellationToken)) ?? [];
        favorites.RemoveAll(x => string.Equals(x.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        favorites.Add(new(name.Trim(), region));
        await settings.SetAsync(FavoritesKey, favorites, cancellationToken);
    }

    public async Task<IReadOnlyList<FavoriteCaptureRegion>> ListFavoritesAsync(CancellationToken cancellationToken = default) =>
        (await settings.GetAsync<List<FavoriteCaptureRegion>>(FavoritesKey, cancellationToken)) ?? [];

    private static void Validate(PersistentCaptureRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (!ScreenCaptureGeometry.IsValid(region.Bounds)) throw new ArgumentOutOfRangeException(nameof(region));
        if (string.IsNullOrWhiteSpace(region.MonitorDeviceName)) throw new ArgumentException("Monitor requerido.", nameof(region));
        if (region.DpiX == 0 || region.DpiY == 0) throw new ArgumentOutOfRangeException(nameof(region), "DPI debe ser positivo.");
    }
}
