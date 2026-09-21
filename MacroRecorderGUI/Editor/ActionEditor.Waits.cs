using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using ProtobufGenerated;

namespace MacroRecorderGUI.Editor;

public sealed partial class ActionEditor
{
    public WaitConditionEvent InsertWait(RecordedAction? after, WaitCondition condition)
    {
        Refresh();
        if (after is not null) RequireCurrent(after);
        var index = after?.End ?? 0;
        var input = new WaitConditionEvent(condition);
        WaitValidation.ValidateSchedule(_macro.Events.Take(index).Append(input));
        Execute("Insert conditional wait", () =>
        {
            _macro.Events.Insert(index, input);
            _macro.ReplaceSelection([input]);
            RawSelection = false;
        });
        return input;
    }

    public WaitConditionEvent ReplaceDelayWithWait(RecordedAction action, WaitCondition condition)
    {
        RequireCurrent(action);
        var input = new WaitConditionEvent(condition);
        WaitValidation.ValidateSchedule(_macro.Events.Take(action.Start).Append(input));
        Execute("Replace fixed delay with conditional wait", () =>
        {
            action.First.TimeSinceLastEvent = 0;
            _macro.Events.Insert(action.Start, input);
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
