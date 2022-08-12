using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using MacroRecorderGUI.Common;

namespace MacroRecorderGUI.Utils.Tests
{
    [TestClass()]
    public class MouseActionTypeConverterTests
    {
        [TestMethod()]
        public void ToStringTest()
        {
            Assert.AreEqual("RightDown", MouseActionTypeConverter.ToString(MouseActionTypeFlags.RightDown));
            Assert.AreEqual("LeftDown, RightDown", MouseActionTypeConverter.ToString(MouseActionTypeFlags.LeftDown | MouseActionTypeFlags.RightDown));
        }

        [TestMethod()]
        public void FromStringTest()
        {
            Assert.AreEqual(MouseActionTypeFlags.RightDown, MouseActionTypeConverter.FromString("RightDown"));
            Assert.AreEqual(MouseActionTypeFlags.LeftDown | MouseActionTypeFlags.RightDown, MouseActionTypeConverter.FromString("LeftDown, RightDown"));
            Assert.AreEqual(MouseActionTypeFlags.LeftDown | MouseActionTypeFlags.RightDown, MouseActionTypeConverter.FromString("LeftDown,RightDown"));
            Assert.AreEqual(MouseActionTypeFlags.LeftDown | MouseActionTypeFlags.RightDown, MouseActionTypeConverter.FromString("LeftDown , RightDown"));
        }
        [TestMethod()]
        [ExpectedException(typeof(ArgumentException))]
        public void FromStringTestThrowOnMalformedString()
        {
            Assert.AreEqual((uint)0xA, MouseActionTypeConverter.FromString("LeftDown|RightDown"));
        }
    }
}