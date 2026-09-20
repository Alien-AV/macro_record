using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class EmergencyStopWorkflowTests
{
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
