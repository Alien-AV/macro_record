using System;
using MacroRecorderGUI.Common;

namespace MacroRecorderGUI.Utils
{
    public static class MouseActionTypeConverter
    {
        public static string ToString(MouseActionTypeFlags actionType)
        {
            return actionType.ToString();
        }

        public static MouseActionTypeFlags FromString(string value)
        {
            return (MouseActionTypeFlags)Enum.Parse(typeof(MouseActionTypeFlags), value);
        }
    }
}