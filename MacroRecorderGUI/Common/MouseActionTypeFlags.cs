using System;

namespace MacroRecorderGUI.Common
{
    [Flags]
    public enum MouseActionTypeFlags // todo: also relevant in other places? should be used instead of the uint currently in use?
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
}