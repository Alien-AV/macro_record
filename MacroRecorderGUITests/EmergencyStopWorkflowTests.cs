using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class EmergencyStopWorkflowTests
{
    [TestMethod]
    public void SelectingSameGestureReplacesHandlerWithoutReleasingOrRegisteringAgain()
    {
        var registrationCalls = 0;
        var releaseCalls = 0;
        var originalCalls = 0;
        var commands = new List<RecordingStopCommand>();
        using var hotkeys = new GlobalHotkeys((_, _, _) => { registrationCalls++; return true; },
            _ => { releaseCalls++; return true; });
        var gesture = KeyboardShortcuts.EmergencyStopChoices[0];
        Assert.IsTrue(hotkeys.TrySetEmergencyStop(gesture, _ => originalCalls++, out _));

        Assert.IsTrue(hotkeys.TrySetEmergencyStop(new HotkeyGesture(gesture.Key, gesture.Modifiers), commands.Add, out var error));

        Assert.IsNull(error);
        Assert.AreEqual(1, registrationCalls);
        Assert.AreEqual(0, releaseCalls);
        Assert.IsTrue(hotkeys.DispatchHotkey(9000, uint.MaxValue));
        Assert.AreEqual(0, originalCalls);
        Assert.AreEqual(new RecordingStopCommand(RecordingStopGestures.ControlR, uint.MaxValue), commands.Single());
    }

    [TestMethod]
    public void UnsupportedGestureLeavesExistingEmergencyStopAndHandlerIntact()
    {
        var registered = new HashSet<int>();
        var registrationCalls = 0;
        var commands = new List<RecordingStopCommand>();
        using var hotkeys = new GlobalHotkeys((id, _, _) => { registrationCalls++; return registered.Add(id); }, registered.Remove);
        var original = KeyboardShortcuts.EmergencyStopChoices[0];
        Assert.IsTrue(hotkeys.TrySetEmergencyStop(original, commands.Add, out _));

        Assert.IsFalse(hotkeys.TrySetEmergencyStop(new HotkeyGesture(VirtualKey.W, HotKeyModifiers.Control),
            _ => Assert.Fail("An unsupported replacement must not change the handler."), out var error));

        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
        Assert.AreEqual(1, registrationCalls);
        Assert.AreEqual(original, hotkeys.EmergencyStop);
        Assert.AreEqual(RecordingStopGestures.ControlR, hotkeys.RegisteredRecordingStops);
        Assert.IsTrue(hotkeys.DispatchHotkey(registered.Single(), 123));
        Assert.AreEqual(new RecordingStopCommand(RecordingStopGestures.ControlR, 123), commands.Single());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NullEmergencyHandlerCannotReplaceOrReleaseTheWorkingHandler(bool sameGesture)
    {
        var registered = new HashSet<int>();
        var registrationCalls = 0;
        var commands = new List<RecordingStopCommand>();
        using var hotkeys = new GlobalHotkeys((id, _, _) => { registrationCalls++; return registered.Add(id); }, registered.Remove);
        var original = KeyboardShortcuts.EmergencyStopChoices[0];
        Assert.IsTrue(hotkeys.TrySetEmergencyStop(original, commands.Add, out _));

        var error = Assert.ThrowsExactly<ArgumentNullException>(() => hotkeys.TrySetEmergencyStop(
            KeyboardShortcuts.EmergencyStopChoices[sameGesture ? 0 : 1], null!, out _));

        Assert.AreEqual("handler", error.ParamName);
        Assert.AreEqual(1, registrationCalls);
        Assert.AreEqual(original, hotkeys.EmergencyStop);
        Assert.AreEqual(RecordingStopGestures.ControlR, hotkeys.RegisteredRecordingStops);
        Assert.IsTrue(hotkeys.DispatchHotkey(registered.Single(), 456));
        Assert.AreEqual(new RecordingStopCommand(RecordingStopGestures.ControlR, 456), commands.Single());
    }

    [TestMethod]
    public void FailedRollbackRetainsBothStopsThroughLaterReplacementAndDisposalRetriesRelease()
    {
        var registered = new HashSet<int>();
        var failRelease = false;
        var released = new List<int>();
        var commands = new List<RecordingStopCommand>();
        using var hotkeys = new GlobalHotkeys((id, _, _) => registered.Add(id), id =>
        {
            released.Add(id);
            return !failRelease && registered.Remove(id);
        });
        Assert.IsTrue(hotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[0], commands.Add, out _));
        var originalId = registered.Single();
        failRelease = true;
        Assert.IsFalse(hotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[1], commands.Add, out var error));
        Assert.IsNotNull(error);
        var retainedId = registered.Single(id => id != originalId);
        CollectionAssert.AreEqual(new[] { originalId, retainedId }, released);
        Assert.AreEqual(KeyboardShortcuts.EmergencyStopChoices[0], hotkeys.EmergencyStop);
        Assert.AreEqual(RecordingStopGestures.ControlR | RecordingStopGestures.ControlAltF12, hotkeys.RegisteredRecordingStops);
        Assert.IsTrue(hotkeys.DispatchHotkey(originalId, 11));
        Assert.IsTrue(hotkeys.DispatchHotkey(retainedId, 22));

        failRelease = false;
        Assert.IsTrue(hotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[2], commands.Add, out error));
        Assert.IsNull(error);
        var currentId = registered.Single(id => id != retainedId);
        Assert.IsFalse(hotkeys.DispatchHotkey(originalId, 33));
        Assert.IsTrue(hotkeys.DispatchHotkey(retainedId, 44));
        Assert.IsTrue(hotkeys.DispatchHotkey(currentId, 55));
        Assert.AreEqual(RecordingStopGestures.ControlAltF12 | RecordingStopGestures.ControlShiftF12, hotkeys.RegisteredRecordingStops);
        CollectionAssert.AreEqual(new[]
        {
            new RecordingStopCommand(RecordingStopGestures.ControlR, 11),
            new RecordingStopCommand(RecordingStopGestures.ControlAltF12, 22),
            new RecordingStopCommand(RecordingStopGestures.ControlAltF12, 44),
            new RecordingStopCommand(RecordingStopGestures.ControlShiftF12, 55)
        }, commands);

        released.Clear();
        hotkeys.Dispose();
        CollectionAssert.AreEquivalent(new[] { retainedId, currentId }, released);
        Assert.IsEmpty(registered);
        Assert.IsFalse(hotkeys.DispatchHotkey(retainedId, 66));
        Assert.IsFalse(hotkeys.DispatchHotkey(currentId, 77));
        Assert.AreEqual(4, commands.Count);
    }

    [TestMethod]
    public void FailedReplacementKeepsPreviousRegistrationAndDisplayedGesture()
    {
        var registered = new HashSet<int>();
        var reject = false;
        using var hotkeys = new GlobalHotkeys((id, _, _) => !reject && registered.Add(id), registered.Remove);
        var original = KeyboardShortcuts.EmergencyStopChoices[0];
        Assert.IsTrue(hotkeys.TrySetEmergencyStop(original, _ => { }, out _));
        reject = true;
        Assert.IsFalse(hotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[1], _ => { }, out var error));
        Assert.IsNotNull(error);
        Assert.AreEqual(original, hotkeys.EmergencyStop);
        Assert.AreEqual(1, registered.Count);
        reject = false;
        Assert.IsTrue(hotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[1], _ => { }, out error));
        Assert.IsNull(error);
        Assert.AreEqual(1, registered.Count);
        Assert.AreEqual("Ctrl + Alt + F12", hotkeys.EmergencyStop!.DisplayName);
        hotkeys.Dispose();
        Assert.AreEqual(0, registered.Count);
    }

    [TestMethod]
    public void FailureToReleaseOldShortcutRollsBackNewShortcut()
    {
        var registered = new HashSet<int>();
        var failRelease = false;
        using var hotkeys = new GlobalHotkeys((id, _, _) => registered.Add(id), id =>
            !(failRelease && id == 9000) && registered.Remove(id));
        Assert.IsTrue(hotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[0], _ => { }, out _));
        failRelease = true;
        Assert.IsFalse(hotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[1], _ => { }, out var error));
        Assert.IsNotNull(error);
        CollectionAssert.AreEqual(new[] { 9000 }, registered.ToArray());
        Assert.AreEqual(KeyboardShortcuts.EmergencyStopChoices[0], hotkeys.EmergencyStop);
        failRelease = false;
    }
}
