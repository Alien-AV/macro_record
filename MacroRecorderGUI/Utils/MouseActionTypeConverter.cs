using System;

namespace MacroRecorderGUI.Utils
{
    public static class MouseActionTypeConverter
    {
        [Flags]
        enum ActionTypeFlags // todo: also relevant in other places? should have its own place in Common, and used from there by all?
        {
            Move = 0x1,
            LeftDown = 0x2,
            LeftUp = 0x4,
            RightDown = 0x8,
            RightUp = 0x10,
            MiddleDown = 0x20,
            MiddleUp = 0x40,
            XDown = 0x80,
            XUp = 0x100
        }
        
        public static string ToString(uint actionType)
        {
            return ((ActionTypeFlags)actionType).ToString();
        }

        public static uint FromString(string value)
        {
            return (uint)(ActionTypeFlags)Enum.Parse(typeof(ActionTypeFlags), value);
        }
    }
}