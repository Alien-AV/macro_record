using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using ProtobufGenerated;

namespace MacroRecorderGUI.Editor;

public sealed partial class ActionEditor
{
    public WaitConditionEvent InsertWait(RecordedAction? after, WaitCondition condition, InputEvent? afterDelay = null)
    {
        Refresh();
        if (after is not null) RequireCurrent(after);
        var index = afterDelay is null ? after?.End ?? 0 : _macro.Events.IndexOf(afterDelay);
        if (index < 0) throw new ArgumentException("The selected delay is no longer available.");
        var input = new WaitConditionEvent(condition);
        WaitValidation.ValidateSchedule(_macro.Events.Take(index).Append(input));
        InsertAuthored(after, "Insert conditional wait", [input], afterDelay);
        return input;
    }

    public WaitConditionEvent ReplaceDelayWithWait(RecordedAction action, WaitCondition condition, InputEvent? leadingDelay = null)
    {
        RequireCurrent(action);
        var index = leadingDelay is null ? action.Start : _macro.Events.IndexOf(leadingDelay);
        if (index < 0) throw new ArgumentException("The selected delay is no longer available.");
        var input = new WaitConditionEvent(condition);
        WaitValidation.ValidateSchedule(_macro.Events.Take(index).Append(input));
        Execute("Replace fixed delay with conditional wait", () =>
        {
            if (leadingDelay is null && action.First is DelayEvent delay)
            {
                input.TimeSinceLastEvent = delay.TimeSinceLastEvent;
                _macro.Events.RemoveAt(index);
            }
            else (leadingDelay ?? action.First).TimeSinceLastEvent = 0;
            _macro.Events.Insert(index, input);
            _macro.ReplaceSelection([input]);
            RawSelection = false;
        });
        return input;
    }

    public void SetCondition(RecordedAction action, WaitCondition condition)
    {
        RequireCurrent(action);
        if (action.First is not WaitConditionEvent wait) throw new ArgumentException("Select a conditional wait.");
        WaitValidation.Validate(condition);
        Execute("Change condition", () => wait.SetCondition(condition));
    }
}
