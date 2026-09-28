using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WaitCaptureHotkeysTests
{
    internal sealed class FakeRegistration : IWaitCaptureRegistration
    {
        public readonly Dictionary<int, (HotkeyGesture Gesture, Action<uint> Callback)> Live = [];
        public readonly Dictionary<int, Action<uint>> Callbacks = [];
        public readonly HashSet<int> RefuseRelease = [];
        public HotkeyGesture? Conflict;
        private int _next;
        public int Attempts;
        public bool Register(HotkeyGesture gesture, Action<uint> callback, out int id)
        {
            Attempts++; id = ++_next;
            if (gesture == Conflict || Live.Values.Any(value => value.Gesture == gesture)) return false;
            Live.Add(id, (gesture, callback)); Callbacks.Add(id, callback); return true;
        }
        public bool Unregister(int id) => !RefuseRelease.Contains(id) && Live.Remove(id);
        public void FireAll(uint time) { foreach (var callback in Callbacks.Values.ToArray()) callback(time); }
    }
    private static WaitCaptureConfiguration Enabled() => WaitCaptureConfiguration.Default with
    {
        FocusedWindow = WaitCaptureConfiguration.Default.FocusedWindow with { Enabled = true },
        HoveredWindow = WaitCaptureConfiguration.Default.HoveredWindow with { Enabled = true },
        PointerPixel = WaitCaptureConfiguration.Default.PointerPixel with { Enabled = true }
    };

    [TestMethod]
    public void OffByDefaultConfiguredNoRepeatDispatchAndDisableIgnoreQueuedCallbacks()
    {
        var registration = new FakeRegistration(); var captured = new List<(WaitCaptureTarget, uint)>();
        using var keys = new WaitCaptureHotkeys(registration, (target, time) => captured.Add((target, time)), () => 10);
        Assert.IsTrue(keys.TryApply(WaitCaptureConfiguration.Default, out _)); Assert.AreEqual(0, registration.Attempts);
        Assert.IsTrue(keys.TryApply(Enabled(), out _)); Assert.AreEqual(3, keys.RegisteredBindings.Count);
        registration.FireAll(9); Assert.IsEmpty(captured);
        registration.FireAll(10); registration.FireAll(10);
        CollectionAssert.AreEquivalent(new[] { (WaitCaptureTarget.FocusedWindow, 10u), (WaitCaptureTarget.HoveredWindow, 10u), (WaitCaptureTarget.PointerPixel, 10u) }, captured);
        Assert.IsTrue(keys.TryApply(WaitCaptureConfiguration.Default, out _));
        registration.FireAll(11); Assert.HasCount(3, captured); Assert.IsEmpty(registration.Live);
    }

    [TestMethod]
    public void ConflictRollsBackStagedRegistrationsWithoutChangingExistingConfiguration()
    {
        var registration = new FakeRegistration(); var captured = new List<WaitCaptureTarget>();
        using var keys = new WaitCaptureHotkeys(registration, (target, _) => captured.Add(target), () => 0);
        var original = Enabled() with { HoveredWindow = Enabled().HoveredWindow with { Enabled = false }, PointerPixel = Enabled().PointerPixel with { Enabled = false } };
        Assert.IsTrue(keys.TryApply(original, out _));
        registration.Conflict = Enabled().PointerPixel.Gesture;
        Assert.IsFalse(keys.TryApply(Enabled(), out var error)); Assert.IsNotNull(error);
        Assert.AreEqual(original, keys.Configuration); Assert.IsTrue(keys.IsReady); Assert.HasCount(1, registration.Live);
        registration.FireAll(42); CollectionAssert.AreEqual(new[] { WaitCaptureTarget.FocusedWindow }, captured);
    }

    [TestMethod]
    public void UnregisterFailureRestoresPreviousBindingsAndFailedRollbackIsInertAndRetryable()
    {
        var registration = new FakeRegistration(); var calls = 0;
        using var keys = new WaitCaptureHotkeys(registration, (_, _) => calls++, () => 0);
        Assert.IsTrue(keys.TryApply(Enabled(), out _));
        var ids = registration.Live.Keys.ToArray(); registration.RefuseRelease.Add(ids[1]);
        Assert.IsFalse(keys.TryApply(WaitCaptureConfiguration.Default, out _));
        Assert.AreEqual(Enabled(), keys.Configuration); Assert.HasCount(3, registration.Live); Assert.IsTrue(keys.IsReady);
        registration.FireAll(5); Assert.AreEqual(3, calls);
        // A release failure AND restoration conflict must not leave any callback armed.
        registration.Conflict = registration.Live.First().Value.Gesture;
        var lastId = registration.Live.Keys.Last(); registration.RefuseRelease.Clear(); registration.RefuseRelease.Add(lastId);
        Assert.IsFalse(keys.TryApply(WaitCaptureConfiguration.Default, out _));
        Assert.IsFalse(keys.IsReady); Assert.IsEmpty(keys.RegisteredBindings);
        registration.FireAll(6); Assert.AreEqual(3, calls);
        registration.Conflict = null; registration.RefuseRelease.Clear();
        Assert.IsTrue(keys.TryApply(Enabled(), out _)); Assert.HasCount(3, keys.RegisteredBindings);
    }

    [TestMethod]
    public void DisposalRetriesFailedCleanupWithoutCallbacks()
    {
        var registration = new FakeRegistration(); var calls = 0;
        var keys = new WaitCaptureHotkeys(registration, (_, _) => calls++, () => 0);
        Assert.IsTrue(keys.TryApply(Enabled(), out _)); var id = registration.Live.Keys.First();
        registration.RefuseRelease.Add(id);
        keys.Dispose(); Assert.HasCount(1, registration.Live); Assert.IsFalse(keys.IsReady);
        registration.FireAll(20); Assert.AreEqual(0, calls);
        registration.RefuseRelease.Clear(); keys.Dispose(); Assert.IsEmpty(registration.Live);
        registration.FireAll(21); Assert.AreEqual(0, calls);
        Assert.ThrowsExactly<ObjectDisposedException>(() => keys.TryApply(Enabled(), out _));
    }

    [TestMethod]
    public void ChordSwapReusesRegistrationsButQueuedOldConfigurationMessagesAreIgnored()
    {
        var registration = new FakeRegistration(); var calls = new List<WaitCaptureTarget>(); uint clock = uint.MaxValue - 3;
        using var keys = new WaitCaptureHotkeys(registration, (target, _) => calls.Add(target), () => clock);
        var original = Enabled(); Assert.IsTrue(keys.TryApply(original, out _));
        var first = registration.Live.First(pair => pair.Value.Gesture == original.FocusedWindow.Gesture).Value.Callback;
        first(uint.MaxValue - 2); Assert.AreEqual(WaitCaptureTarget.FocusedWindow, calls.Single());
        clock = 1;
        Assert.IsTrue(keys.TryApply(original with { FocusedWindow = original.HoveredWindow, HoveredWindow = original.FocusedWindow }, out _));
        first(uint.MaxValue); Assert.HasCount(1, calls); first(2); Assert.AreEqual(WaitCaptureTarget.HoveredWindow, calls.Last());
        Assert.AreEqual(3, registration.Attempts);
        clock = 3; keys.DiscardPendingMessages();
        first(3); Assert.HasCount(2, calls); first(4); Assert.HasCount(3, calls);
    }

    [TestMethod]
    public void InvalidOrReservedChordsAreRejectedBeforeAnyRegistration()
    {
        var registration = new FakeRegistration();
        using var keys = new WaitCaptureHotkeys(registration, (_, _) => { }, () => 0);
        foreach (var gesture in KeyboardShortcuts.EmergencyStopChoices.Concat(new[] { KeyboardShortcuts.StartRecording,
            KeyboardShortcuts.StopRecording, KeyboardShortcuts.StartPlayback, new HotkeyGesture(VirtualKey.A, HotKeyModifiers.None),
            new HotkeyGesture(VirtualKey.Shift, HotKeyModifiers.Control), new HotkeyGesture(VirtualKey.F8, (HotKeyModifiers)0x4002) }))
            Assert.ThrowsExactly<ArgumentException>(() => keys.TryApply(Enabled() with { FocusedWindow = new(true, gesture.Key, gesture.Modifiers) }, out _));
        Assert.ThrowsExactly<ArgumentException>(() => keys.TryApply(Enabled() with { PointerPixel = Enabled().FocusedWindow }, out _));
        Assert.AreEqual(0, registration.Attempts);
    }

    [TestMethod]
    public void FailedStagingCleanupKeepsLeakedIdsTrackedButInertUntilRetry()
    {
        var registration = new FakeRegistration(); var calls = 0;
        using var keys = new WaitCaptureHotkeys(registration, (_, _) => calls++, () => 0);
        registration.RefuseRelease.Add(1); registration.Conflict = Enabled().HoveredWindow.Gesture;
        Assert.IsFalse(keys.TryApply(Enabled(), out var error)); StringAssert.Contains(error!, "Cleanup failed");
        Assert.HasCount(1, registration.Live); Assert.IsTrue(keys.HasRegistrations); Assert.IsFalse(keys.IsReady);
        registration.FireAll(50); Assert.AreEqual(0, calls);
        Assert.IsFalse(keys.TryApply(WaitCaptureConfiguration.Default, out _));
        registration.RefuseRelease.Clear(); registration.Conflict = null;
        Assert.IsTrue(keys.TryApply(WaitCaptureConfiguration.Default, out _)); Assert.IsEmpty(registration.Live);
        registration.FireAll(51); Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void CaptureRegistrationSharesExistingLifecycleWithoutRemovingRecordOrEmergencyStop()
    {
        var registered = new HashSet<int>(); var records = 0; var stops = 0; var captures = 0;
        using var global = new GlobalHotkeys((id, _, _) => registered.Add(id), registered.Remove);
        Assert.IsTrue(global.AddHotKey(KeyboardShortcuts.StartRecording.Key, KeyboardShortcuts.StartRecording.Modifiers, _ => records++));
        Assert.IsTrue(global.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[0], _ => stops++, out _));
        var shellIds = registered.ToArray();
        using var keys = new WaitCaptureHotkeys(global, (_, _) => captures++, () => 0);
        Assert.IsTrue(keys.TryApply(Enabled(), out _)); var captureIds = registered.Except(shellIds).ToArray();
        foreach (var id in captureIds) Assert.IsTrue(global.DispatchHotkey(id, 8));
        Assert.AreEqual(3, captures); Assert.AreEqual(RecordingStopGestures.ControlR, global.RegisteredRecordingStops);
        keys.Dispose(); CollectionAssert.AreEquivalent(shellIds, registered.ToArray());
        foreach (var id in captureIds) Assert.IsFalse(global.DispatchHotkey(id, 9));
        foreach (var id in shellIds) Assert.IsTrue(global.DispatchHotkey(id, 10));
        Assert.AreEqual(1, records); Assert.AreEqual(1, stops);
    }
}
