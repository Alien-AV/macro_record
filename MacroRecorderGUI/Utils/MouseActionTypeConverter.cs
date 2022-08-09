using System;
using MacroRecorderGUI.Common;

namespace MacroRecorderGUI.Utils
{
    public static class MouseActionTypeConverter
    {
        public static string ToString(uint actionType)
        {
            return ((MouseActionTypeFlags)actionType).ToString();
        }

        public static uint FromString(string value)
        {
            return (uint)(MouseActionTypeFlags)Enum.Parse(typeof(MouseActionTypeFlags), value);
        }
    }
}