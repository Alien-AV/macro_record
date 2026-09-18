using MacroRecorderGUI.Models;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public class HotkeyChordTests
{
    [TestMethod]
    [DataRow(0x11u, false)]
    [DataRow(0x11u, true)]
    [DataRow(0xA2u, false)]
    [DataRow(0xA2u, true)]
    [DataRow(0xA3u, false)]
    [DataRow(0xA3u, true)]
    public void ReleasesDrainInEitherOrderAndLaterChordIsRecorded(uint control, bool controlFirst)
    {
        var filter = new RecordingStartChord(RecordingStartKeys.Q | HeldControl(control));
        var first = controlFirst ? control : 0x51u;
        var second = controlFirst ? 0x51u : control;

        Assert.IsNull(filter.Accept(Key(first, false, 10)));
        Assert.IsNull(filter.Accept(Key(second, false, 20)));
        Assert.IsNull(filter.Accept(Key(first, true, 30)));
        Assert.IsNull(filter.Accept(Key(second, false, 40)));
        Assert.IsNull(filter.Accept(Key(second, true, 50)));
        var next = filter.Accept(Key(control, false, 60))!;
        Assert.AreEqual(210ul, next.TimeSinceLastEvent);
        Assert.IsNotNull(filter.Accept(Key(0x51, false)));
        Assert.IsNotNull(filter.Accept(Key(0x51, true)));
        Assert.IsNotNull(filter.Accept(Key(control, true)));
    }

    [TestMethod]
    [DataRow(RecordingStartKeys.Control, 0xA2u)]
    [DataRow(RecordingStartKeys.Control, 0xA3u)]
    [DataRow(RecordingStartKeys.LeftControl, 0x11u)]
    [DataRow(RecordingStartKeys.RightControl, 0x11u)]
    public void GenericAndSidedControlAliasesDrain(RecordingStartKeys held, uint release)
    {
        var filter = new RecordingStartChord(held);
        Assert.IsNull(filter.Accept(Key(release, false)));
        Assert.IsNull(filter.Accept(Key(release, true)));
        Assert.IsNotNull(filter.Accept(Key(release, false)));
        Assert.IsNotNull(filter.Accept(Key(release, true)));
    }

    [TestMethod]
    [DataRow(0xA2u, 0xA3u)]
    [DataRow(0xA3u, 0xA2u)]
    public void BothControlKeysDrainIndependently(uint first, uint second)
    {
        var filter = new RecordingStartChord(RecordingStartKeys.LeftControl | RecordingStartKeys.RightControl);
        Assert.IsNull(filter.Accept(Key(first, true)));
        Assert.IsNotNull(filter.Accept(Key(first, false)));
        Assert.IsNotNull(filter.Accept(Key(first, true)));
        Assert.IsNull(filter.Accept(Key(second, false)));
        Assert.IsNull(filter.Accept(Key(second, true)));
        Assert.IsNotNull(filter.Accept(Key(second, false)));
    }

    [TestMethod]
    public void UnrelatedInputAndReleasedKeyReusePassWhileOtherCommandKeyDrains()
    {
        var filter = new RecordingStartChord(RecordingStartKeys.Q | RecordingStartKeys.LeftControl);
        Assert.IsNull(filter.Accept(Key(0x51, true, 10)));
        var mouse = Mouse(20);
        Assert.AreEqual(30ul, filter.Accept(mouse)!.TimeSinceLastEvent);
        Assert.AreEqual(20ul, mouse.TimeSinceLastEvent, "Filtering must not mutate the native event.");
        Assert.IsNotNull(filter.Accept(Key(0x51, false)));
        Assert.IsNotNull(filter.Accept(Key(0x51, true)));
        Assert.IsNotNull(filter.Accept(Key(0xA3, false)));
        Assert.IsNotNull(filter.Accept(Key(0xA3, true)));
        Assert.IsNotNull(filter.Accept(Key(0x41, false)));
        Assert.IsNotNull(filter.Accept(Key(0x41, true)));
        Assert.IsNull(filter.Accept(Key(0xA2, false, 30)));
        Assert.IsNull(filter.Accept(Key(0xA2, true, 40)));
        Assert.AreEqual(120ul, filter.Accept(Mouse(50))!.TimeSinceLastEvent);
    }

    [TestMethod]
    public void ButtonStartAndAlreadyReleasedShortcutKeysRemainUnfiltered()
    {
        var filter = new RecordingStartChord(RecordingStartKeys.None);
        foreach (var inputEvent in new[] { Mouse(0), Key(0x11, true), Key(0x51, false), Key(0x51, false), Key(0x51, true) })
        {
            Assert.AreSame(inputEvent, filter.Accept(inputEvent));
        }
    }

    [TestMethod]
    public void OnlyKeysHeldAtReadinessAreSuppressed()
    {
        var filter = new RecordingStartChord(RecordingStartKeys.LeftControl);
        var position = Mouse(0);
        Assert.AreSame(position, filter.Accept(position));
        Assert.IsNotNull(filter.Accept(Key(0x51, false)));
        Assert.IsNotNull(filter.Accept(Key(0x51, true)));
        Assert.IsNull(filter.Accept(Key(0xA2, true)));
    }

    [TestMethod]
    public void SuppressedDelaysDoNotWrapAround()
    {
        var filter = new RecordingStartChord(RecordingStartKeys.Q);
        Assert.IsNull(filter.Accept(Key(0x51, false, ulong.MaxValue)));
        Assert.IsNull(filter.Accept(Key(0x51, true, 1)));
        Assert.AreEqual(ulong.MaxValue, filter.Accept(Mouse(1))!.TimeSinceLastEvent);
    }

    private static RecordingStartKeys HeldControl(uint key) => key switch
    {
        0xA2 => RecordingStartKeys.LeftControl,
        0xA3 => RecordingStartKeys.RightControl,
        _ => RecordingStartKeys.Control
    };

    internal static ProtobufInputEvent Key(uint key, bool up, ulong delay = 0) => new()
    {
        KeyboardEvent = new() { VirtualKeyCode = key, KeyUp = up },
        TimeSinceLastEvent = delay
    };

    internal static ProtobufInputEvent Mouse(ulong delay) => new()
    {
        MouseEvent = new() { ActionType = 1, X = 10, Y = 20 },
        TimeSinceLastEvent = delay
    };
}
