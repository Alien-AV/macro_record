using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class GlobalHotkeysTests
{
    [TestMethod]
    public void NullHandlerIsRejectedBeforeRegisteringAHotkey()
    {
        var registrationCalls = 0;
        using var hotkeys = new GlobalHotkeys((_, _, _) => { registrationCalls++; return true; }, _ => true);

        var error = Assert.ThrowsExactly<ArgumentNullException>(() =>
            hotkeys.AddHotKey(VirtualKey.W, HotKeyModifiers.Control, null!));

        Assert.AreEqual("handler", error.ParamName);
        Assert.AreEqual(0, registrationCalls);
        Assert.AreEqual(RecordingStopGestures.None, hotkeys.RegisteredRecordingStops);
        Assert.IsFalse(hotkeys.DispatchHotkey(9000, 123));
    }

    [TestMethod]
    public void OrdinaryHotkeyDispatchPreservesMessageTimeWithoutAddingAStopGesture()
    {
        var registered = new HashSet<int>();
        var times = new List<uint>();
        using var hotkeys = new GlobalHotkeys((id, key, modifiers) =>
        {
            Assert.AreEqual(VirtualKey.Q, key);
            Assert.AreEqual(HotKeyModifiers.Control, modifiers);
            return registered.Add(id);
        }, registered.Remove);

        Assert.IsTrue(hotkeys.AddHotKey(VirtualKey.Q, HotKeyModifiers.Control, times.Add));
        Assert.IsTrue(hotkeys.DispatchHotkey(registered.Single(), uint.MaxValue));
        Assert.IsFalse(hotkeys.DispatchHotkey(-1, 0));
        CollectionAssert.AreEqual(new[] { uint.MaxValue }, times);
        Assert.AreEqual(RecordingStopGestures.None, hotkeys.RegisteredRecordingStops);
    }

    [TestMethod]
    public void DisposalReleasesEveryRegistrationOnceAndRejectsLateDispatchAndNewRegistrations()
    {
        var registered = new HashSet<int>();
        var released = new List<int>();
        var invoked = false;
        using var hotkeys = new GlobalHotkeys((id, _, _) => registered.Add(id), id =>
        {
            released.Add(id);
            return registered.Remove(id);
        });
        Assert.IsTrue(hotkeys.AddHotKey(VirtualKey.W, HotKeyModifiers.Control, _ => invoked = true));
        Assert.IsTrue(hotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[0], _ => invoked = true, out _));
        var ids = registered.ToArray();

        hotkeys.Dispose();
        hotkeys.Dispose();

        CollectionAssert.AreEquivalent(ids, released);
        Assert.IsEmpty(registered);
        foreach (var id in ids) Assert.IsFalse(hotkeys.DispatchHotkey(id, 42));
        Assert.IsFalse(invoked);
        Assert.IsNull(hotkeys.EmergencyStop);
        Assert.AreEqual(RecordingStopGestures.None, hotkeys.RegisteredRecordingStops);
        Assert.ThrowsExactly<ObjectDisposedException>(() =>
            hotkeys.AddHotKey(VirtualKey.W, HotKeyModifiers.Control, _ => { }));
        Assert.ThrowsExactly<ObjectDisposedException>(() =>
            hotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[1], _ => { }, out _));
        Assert.IsEmpty(registered);
    }
}
