using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

public interface IGlobalHotkeyPlatformAdapter : IDisposable
{
    event EventHandler<int>? HotkeyPressed;
    bool Register(int id, HotkeyGesture gesture);
    void Unregister(int id);
}

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler<HotkeyAction>? HotkeyInvoked;
    IReadOnlyList<HotkeyBinding> Bindings { get; }
    HotkeyConfigurationResult Apply(IReadOnlyList<HotkeyBinding> bindings);
    void UnregisterAll();
}

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private readonly IGlobalHotkeyPlatformAdapter _platform;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _repeatProtection;
    private readonly Dictionary<int, HotkeyBinding> _registrations = [];
    private DateTimeOffset _lastInvocation = DateTimeOffset.MinValue;
    private bool _disposed;

    public GlobalHotkeyService(
        IGlobalHotkeyPlatformAdapter platform,
        TimeSpan? repeatProtection = null,
        TimeProvider? timeProvider = null)
    {
        _platform = platform;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _repeatProtection = repeatProtection ?? TimeSpan.FromMilliseconds(300);
        _platform.HotkeyPressed += OnHotkeyPressed;
    }

    public event EventHandler<HotkeyAction>? HotkeyInvoked;
    public IReadOnlyList<HotkeyBinding> Bindings => _registrations.Values.ToArray();

    public HotkeyConfigurationResult Apply(IReadOnlyList<HotkeyBinding> bindings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // A configuration change is transactional: never leave the previous set active.
        UnregisterAll();
        var enabled = bindings.Where(binding => binding.IsEnabled).ToArray();
        var duplicates = enabled.GroupBy(binding => binding.Gesture)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(binding => new HotkeyConflict(
                binding.Action, $"El atajo {binding.Gesture} está asignado más de una vez.")))
            .ToArray();
        if (duplicates.Length != 0) return new(false, duplicates);

        var conflicts = new List<HotkeyConflict>();
        foreach (var binding in enabled)
        {
            var id = (int)binding.Action + 1;
            if (_platform.Register(id, binding.Gesture)) _registrations.Add(id, binding);
            else conflicts.Add(new(binding.Action, $"Windows no pudo registrar el atajo {binding.Gesture}. Puede estar en uso por otra aplicación."));
        }

        if (conflicts.Count == 0) return HotkeyConfigurationResult.Success;
        UnregisterAll();
        return new(false, conflicts);
    }

    public void UnregisterAll()
    {
        foreach (var id in _registrations.Keys.ToArray()) _platform.Unregister(id);
        _registrations.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        UnregisterAll();
        _platform.HotkeyPressed -= OnHotkeyPressed;
        _platform.Dispose();
        _disposed = true;
    }

    private void OnHotkeyPressed(object? sender, int id)
    {
        if (!_registrations.TryGetValue(id, out var binding)) return;
        var now = _timeProvider.GetUtcNow();
        if (now - _lastInvocation < _repeatProtection) return;
        _lastInvocation = now;
        HotkeyInvoked?.Invoke(this, binding.Action);
    }
}
