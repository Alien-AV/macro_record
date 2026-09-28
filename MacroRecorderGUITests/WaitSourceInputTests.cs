using MacroRecorderGUI.Views;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WaitSourceInputTests
{
    [TestMethod]
    [DataRow("0", 0UL)]
    [DataRow(" 0XAbCd ", 43981UL)]
    [DataRow("18446744073709551615", ulong.MaxValue)]
    [DataRow("0xFFFFFFFFFFFFFFFF", ulong.MaxValue)]
    public void AddressesKeepFullUnsignedPrecision(string text, ulong expected) =>
        Assert.AreEqual(expected, WaitSourceInput.Address(text, "Base address"));

    [TestMethod]
    [DataRow("")]
    [DataRow("-1")]
    [DataRow("0x")]
    [DataRow("1,000")]
    [DataRow("1.5")]
    [DataRow("18446744073709551616")]
    public void InvalidAddressesNameTheField(string text) =>
        StringAssert.Contains(Assert.ThrowsExactly<ArgumentException>(() => WaitSourceInput.Address(text, "Base address")).Message, "Base address");

    [TestMethod]
    public void OffsetsRetainOrderSignAnd64BitBoundaries()
    {
        var expected = new[] { 32L, -16, long.MinValue, long.MaxValue };
        var offsets = WaitSourceInput.Offsets("0x20, -0x10; -9223372036854775808\r\n0x7FFFFFFFFFFFFFFF", 4);
        CollectionAssert.AreEqual(expected, offsets);
        foreach (var offset in offsets)
            Assert.AreEqual(offset, WaitSourceInput.Offset(WaitSourceInput.FormatOffset(offset), "Offset"));
        Assert.AreEqual(ulong.MaxValue, WaitSourceInput.Address(WaitSourceInput.FormatAddress(ulong.MaxValue), "Address"));
        Assert.AreEqual(0, WaitSourceInput.Offsets("  ", 4).Length);
    }

    [TestMethod]
    [DataRow("0x20,", 2, "Pointer offset 2")]
    [DataRow("1,,2", 3, "Pointer offset 2")]
    [DataRow("0x8000000000000000", 2, "Pointer offset 1")]
    [DataRow("-0x8000000000000001", 2, "Pointer offset 1")]
    [DataRow("1,2,3", 2, "at most 2")]
    public void IncompleteAndOversizedChainsRemainInvalidDrafts(string text, int maximum, string expected) =>
        StringAssert.Contains(Assert.ThrowsExactly<ArgumentException>(() => WaitSourceInput.Offsets(text, maximum)).Message, expected);
}
