using MacroRecorderGUI.Event;

namespace MacroRecorderGUI.Editor;

public sealed partial class ActionEditor
{
    public DelayEvent InsertDelay(RecordedAction? after, ulong microseconds, InputEvent? afterDelay = null)
    {
        var input = new DelayEvent(microseconds);
        InsertAuthored(after, "Insert delay", [input], afterDelay);
        return input;
    }

    public void SetFixedDelay(DelayEvent input, ulong microseconds)
    {
        if (!_macro.Events.Contains(input)) throw new ArgumentException("The delay is no longer available.");
        DelayEvent.ValidateDuration(microseconds);
        Execute("Change delay", () => input.DurationMicroseconds = microseconds);
    }

    public void SetLeadingDelay(InputEvent input, ulong microseconds)
    {
        if (!_macro.Events.Contains(input)) throw new ArgumentException("The delay is no longer available.");
        Execute("Change delay", () => input.TimeSinceLastEvent = microseconds);
    }
}
