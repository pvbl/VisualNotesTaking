using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

public sealed class GlobalHotkeyServiceTests
{
    [Fact]
    public void Duplicate_gestures_are_reported_without_calling_Windows()
    {
        using var adapter = new FakeHotkeyAdapter();
        using var service = new GlobalHotkeyService(adapter);
        var gesture = new HotkeyGesture(HotkeyModifiers.Control, 0x43);

        var result = service.Apply([
            new(HotkeyAction.CaptureRegion, gesture),
            new(HotkeyAction.TogglePause, gesture)
        ]);

        result.Succeeded.ShouldBeFalse();
        result.Conflicts.Count.ShouldBe(2);
        adapter.Registered.ShouldBeEmpty();
    }

    [Fact]
    public void Non_registerable_gesture_rolls_back_every_registration()
    {
        using var adapter = new FakeHotkeyAdapter { RejectedKey = 0x50 };
        using var service = new GlobalHotkeyService(adapter);

        var result = service.Apply([
            Binding(HotkeyAction.CaptureRegion, 0x43),
            Binding(HotkeyAction.TogglePause, 0x50)
        ]);

        result.Succeeded.ShouldBeFalse();
        result.Conflicts.Single().Message.ShouldContain("otra aplicación");
        adapter.Registered.ShouldBeEmpty();
        adapter.Unregistered.ShouldContain((int)HotkeyAction.CaptureRegion + 1);
    }

    [Fact]
    public void Changing_configuration_and_disposal_always_release_registered_hotkeys()
    {
        var adapter = new FakeHotkeyAdapter();
        var service = new GlobalHotkeyService(adapter);
        service.Apply([Binding(HotkeyAction.CaptureRegion, 0x43)]).Succeeded.ShouldBeTrue();

        service.Apply([Binding(HotkeyAction.TogglePause, 0x50)]).Succeeded.ShouldBeTrue();
        adapter.Unregistered.ShouldContain((int)HotkeyAction.CaptureRegion + 1);

        service.Dispose();
        adapter.Unregistered.ShouldContain((int)HotkeyAction.TogglePause + 1);
        adapter.WasDisposed.ShouldBeTrue();
    }

    [Fact]
    public void Repeated_key_messages_inside_protection_interval_are_ignored()
    {
        using var adapter = new FakeHotkeyAdapter();
        var clock = new ManualTimeProvider();
        using var service = new GlobalHotkeyService(adapter, TimeSpan.FromMilliseconds(300), clock);
        var calls = 0;
        service.HotkeyInvoked += (_, _) => calls++;
        service.Apply([Binding(HotkeyAction.CaptureRegion, 0x43)]);

        adapter.Press((int)HotkeyAction.CaptureRegion + 1);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        adapter.Press((int)HotkeyAction.CaptureRegion + 1);
        clock.Advance(TimeSpan.FromMilliseconds(300));
        adapter.Press((int)HotkeyAction.CaptureRegion + 1);

        calls.ShouldBe(2);
    }

    private static HotkeyBinding Binding(HotkeyAction action, uint key) =>
        new(action, new(HotkeyModifiers.Control | HotkeyModifiers.Shift, key));

    private sealed class FakeHotkeyAdapter : IGlobalHotkeyPlatformAdapter
    {
        public event EventHandler<int>? HotkeyPressed;
        public uint? RejectedKey { get; init; }
        public Dictionary<int, HotkeyGesture> Registered { get; } = [];
        public List<int> Unregistered { get; } = [];
        public bool WasDisposed { get; private set; }
        public bool Register(int id, HotkeyGesture gesture)
        {
            if (gesture.VirtualKey == RejectedKey) return false;
            Registered[id] = gesture;
            return true;
        }
        public void Unregister(int id) { Registered.Remove(id); Unregistered.Add(id); }
        public void Press(int id) => HotkeyPressed?.Invoke(this, id);
        public void Dispose() => WasDisposed = true;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan interval) => _now += interval;
    }
}
