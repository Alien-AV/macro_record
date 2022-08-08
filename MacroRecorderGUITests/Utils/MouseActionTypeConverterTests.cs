using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace MacroRecorderGUI.Utils.Tests
{
    [TestClass()]
    public class MouseActionTypeConverterTests
    {
        [TestMethod()]
        public void ToStringTest()
        {
            Assert.AreEqual("RightDown", MouseActionTypeConverter.ToString(0x8));
            Assert.AreEqual("LeftDown, RightDown", MouseActionTypeConverter.ToString(0xA));
        }

        [TestMethod()]
        public void FromStringTest()
        {
            Assert.AreEqual((uint)0x8, MouseActionTypeConverter.FromString("RightDown"));
            Assert.AreEqual((uint)0xA, MouseActionTypeConverter.FromString("LeftDown, RightDown"));
            Assert.AreEqual((uint)0xA, MouseActionTypeConverter.FromString("LeftDown,RightDown"));
            Assert.AreEqual((uint)0xA, MouseActionTypeConverter.FromString("LeftDown , RightDown"));
        }
        [TestMethod()]
        [ExpectedException(typeof(ArgumentException))]
        public void FromStringTestThrowOnMalformedString()
        {
            Assert.AreEqual((uint)0xA, MouseActionTypeConverter.FromString("LeftDown|RightDown"));
        }
    }
}