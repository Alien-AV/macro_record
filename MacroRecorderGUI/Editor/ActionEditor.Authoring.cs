using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using Windows.System;

namespace MacroRecorderGUI.Editor;

public sealed partial class ActionEditor
{
    /// <summary>Insert after a current action, or at the beginning when after is null, as one undoable edit.</summary>
    public IReadOnlyList<InputEvent> InsertClick(RecordedAction? after, ClickDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var (down, up, data) = definition.Button switch
        {
            MouseButton.Left => (MouseActionTypeFlags.LeftDown, MouseActionTypeFlags.LeftUp, 0u),
            MouseButton.Right => (MouseActionTypeFlags.RightDown, MouseActionTypeFlags.RightUp, 0u),
            MouseButton.Middle => (MouseActionTypeFlags.MiddleDown, MouseActionTypeFlags.MiddleUp, 0u),
            MouseButton.X1 => (MouseActionTypeFlags.XDown, MouseActionTypeFlags.XUp, 1u),
            MouseButton.X2 => (MouseActionTypeFlags.XDown, MouseActionTypeFlags.XUp, 2u),
            _ => throw new ArgumentException("Choose Left, Right, Middle, X1, or X2.", nameof(definition))
        };
        return InsertAuthored(after, "Insert click",
        [
            new MouseEvent(0, 0, down) { MouseData = data, RelativePosition = true, MappedToVirtualDesktop = false,
                TimeSinceLastEvent = definition.PauseBeforeMicroseconds },
            new MouseEvent(0, 0, up) { MouseData = data, RelativePosition = true, MappedToVirtualDesktop = false,
                TimeSinceLastEvent = definition.HoldMicroseconds }
        ]);
    }

    /// <summary>Press modifiers in Control/Shift/Alt/Windows order and release in reverse order.</summary>
    public IReadOnlyList<InputEvent> InsertShortcut(RecordedAction? after, ShortcutDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        const ShortcutModifiers supported = ShortcutModifiers.Control | ShortcutModifiers.Shift | ShortcutModifiers.Alt | ShortcutModifiers.Windows;
        if ((definition.Modifiers & ~supported) != 0)
            throw new ArgumentException("Choose only Control, Shift, Alt, and Windows modifiers.", nameof(definition));
        if (!ActionAuthoring.IsSupportedShortcutKey(definition.VirtualKeyCode))
            throw new ArgumentException("Choose a supported keyboard virtual key; specify modifiers separately.", nameof(definition));
        var modifiers = new (ShortcutModifiers Flag, VirtualKey Key)[]
        {
            (ShortcutModifiers.Control, VirtualKey.Control), (ShortcutModifiers.Shift, VirtualKey.Shift),
            (ShortcutModifiers.Alt, VirtualKey.Menu), (ShortcutModifiers.Windows, VirtualKey.LeftWindows)
        }.Where(item => (definition.Modifiers & item.Flag) != 0).Select(item => item.Key).ToArray();
        var inputs = new List<InputEvent>();
        inputs.AddRange(modifiers.Select(key => new KeyboardEvent(key, false)));
        inputs.Add(new KeyboardEvent((VirtualKey)definition.VirtualKeyCode, false));
        inputs.Add(new KeyboardEvent((VirtualKey)definition.VirtualKeyCode, true) { TimeSinceLastEvent = definition.HoldMicroseconds });
        inputs.AddRange(modifiers.Reverse().Select(key => new KeyboardEvent(key, true)));
        inputs[0].TimeSinceLastEvent = definition.PauseBeforeMicroseconds;
        return InsertAuthored(after, "Insert shortcut", inputs.ToArray());
    }

    /// <summary>Insert an explicit pointer report without changing or interpolating captured input.</summary>
    public IReadOnlyList<InputEvent> InsertPointerMovement(RecordedAction? after, PointerMovementDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Space is not (CoordinateSpace.AbsolutePrimary or CoordinateSpace.AbsoluteDesktop or CoordinateSpace.RelativeCounts))
            throw new ArgumentException("Choose primary-screen pixels, virtual-desktop pixels, or relative device counts.", nameof(definition));
        return InsertAuthored(after, "Insert pointer movement",
        [
            new MouseEvent(definition.X, definition.Y, MouseActionTypeFlags.Move)
            {
                RelativePosition = definition.Space == CoordinateSpace.RelativeCounts,
                MappedToVirtualDesktop = definition.Space == CoordinateSpace.AbsoluteDesktop,
                TimeSinceLastEvent = definition.PauseBeforeMicroseconds
            }
        ]);
    }

    private IReadOnlyList<InputEvent> InsertAuthored(RecordedAction? after, string description, InputEvent[] inputs)
    {
        Refresh();
        if (after is not null) RequireCurrent(after);
        var index = after?.End ?? 0;
        RequireReleasedInputs(index);
        Execute(description, () =>
        {
            for (var offset = 0; offset < inputs.Length; offset++) _macro.Events.Insert(index + offset, inputs[offset]);
            _macro.ReplaceSelection(inputs);
            RawSelection = false;
        });
        return Array.AsReadOnly(inputs);
    }

    private void RequireReleasedInputs(int index)
    {
        var keys = new HashSet<uint>();
        uint buttons = 0;
        foreach (var input in _macro.Events.Take(index))
        {
            if (input is KeyboardEvent key)
            {
                if (key.KeyUp) keys.Remove(key.VirtualKeyCode); else keys.Add(key.VirtualKeyCode);
            }
            else if (input is MouseEvent mouse)
            {
                Track(MouseActionTypeFlags.LeftDown, MouseActionTypeFlags.LeftUp, 1);
                Track(MouseActionTypeFlags.RightDown, MouseActionTypeFlags.RightUp, 2);
                Track(MouseActionTypeFlags.MiddleDown, MouseActionTypeFlags.MiddleUp, 4);
                var extra = (mouse.MouseData & 3) << 3;
                // Unknown X-button payloads may hold either button; do not insert a gesture into that state.
                Track(MouseActionTypeFlags.XDown, MouseActionTypeFlags.XUp, extra == 0 ? 24u : extra);
                void Track(MouseActionTypeFlags down, MouseActionTypeFlags up, uint mask)
                {
                    if ((mouse.ActionType & down) != 0) buttons |= mask;
                    if ((mouse.ActionType & up) != 0) buttons &= ~mask;
                }
            }
        }
        if (keys.Count != 0 || buttons != 0)
            throw new ArgumentException("Insert actions only after all recorded keys and mouse buttons are released. Inspect Exact input for incomplete presses.");
    }
}
