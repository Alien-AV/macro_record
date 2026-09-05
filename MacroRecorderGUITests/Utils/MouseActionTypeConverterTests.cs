using MacroRecorderGUI.Common;

namespace MacroRecorderGUI.Utils.Tests;

[TestClass]
public class MouseActionTypeConverterTests
{
    [TestMethod]
    public void ToStringFormatsSingleAndCombinedFlags()
    {
        Assert.AreEqual("RightDown", MouseActionTypeConverter.ToString(MouseActionTypeFlags.RightDown));
        Assert.AreEqual(
            "LeftDown, RightDown",
            MouseActionTypeConverter.ToString(MouseActionTypeFlags.LeftDown | MouseActionTypeFlags.RightDown));
    }

    [TestMethod]
    public void FromStringParsesSingleAndCombinedFlagsWithWhitespace()
    {
        Assert.AreEqual(MouseActionTypeFlags.RightDown, MouseActionTypeConverter.FromString("RightDown"));
        Assert.AreEqual(
            MouseActionTypeFlags.LeftDown | MouseActionTypeFlags.RightDown,
            MouseActionTypeConverter.FromString("LeftDown, RightDown"));
        Assert.AreEqual(
            MouseActionTypeFlags.LeftDown | MouseActionTypeFlags.RightDown,
            MouseActionTypeConverter.FromString("LeftDown,RightDown"));
        Assert.AreEqual(
            MouseActionTypeFlags.LeftDown | MouseActionTypeFlags.RightDown,
            MouseActionTypeConverter.FromString("LeftDown , RightDown"));
    }

    [TestMethod]
    public void FromStringRejectsMalformedSeparators()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => MouseActionTypeConverter.FromString("LeftDown|RightDown"));
    }
}
