using MacroRecorderGUI.Event;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public class KeyboardEventTests
{
    [TestMethod]
    [DataRow("0", 0x30u)]
    [DataRow("1", 0x31u)]
    [DataRow("2", 0x32u)]
    [DataRow("3", 0x33u)]
    [DataRow("4", 0x34u)]
    [DataRow("5", 0x35u)]
    [DataRow("6", 0x36u)]
    [DataRow("7", 0x37u)]
    [DataRow("8", 0x38u)]
    [DataRow("9", 0x39u)]
    [DataRow(" 8 ", 0x38u)]
    public void SingleDigitNamesProduceNumberRowKeys(string name, uint expectedVirtualKey)
    {
        var inputEvent = new KeyboardEvent(VirtualKey.Escape, false) { KeyName = name };

        Assert.AreEqual(expectedVirtualKey, inputEvent.VirtualKeyCode);
        Assert.AreEqual(expectedVirtualKey, inputEvent.OriginalProtobufInputEvent.KeyboardEvent.VirtualKeyCode);
    }

    [TestMethod]
    [DataRow("0x08", 0x08u)]
    [DataRow("0x09", 0x09u)]
    [DataRow("08", 0x08u)]
    [DataRow("09", 0x09u)]
    [DataRow("65", 0x41u)]
    [DataRow("Back", 0x08u)]
    [DataRow("Tab", 0x09u)]
    [DataRow("a", 0x41u)]
    [DataRow("Number8", 0x38u)]
    [DataRow("NumberPad8", 0x68u)]
    public void ExplicitVirtualKeysAndNamedKeysRemainSupported(string name, uint expectedVirtualKey)
    {
        var inputEvent = new KeyboardEvent(VirtualKey.Escape, false) { KeyName = name };

        Assert.AreEqual(expectedVirtualKey, inputEvent.VirtualKeyCode);
    }
}
