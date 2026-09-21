using MacroRecorderGUI.Models;
using ProtobufGenerated;

namespace MacroRecorderGUI.Event;

public sealed class WaitConditionEvent : InputEvent
{
    public WaitConditionEvent(WaitCondition condition) : this(new ProtobufInputEvent { WaitCondition = condition.Clone() }) { }
    public WaitConditionEvent(ProtobufInputEvent input)
    {
        if (input.WaitCondition is null) throw new ArgumentException("A wait condition is required.");
        OriginalProtobufInputEvent = input;
    }
    public WaitCondition Condition => OriginalProtobufInputEvent.WaitCondition.Clone();
    public string Description => WaitValidation.Describe(OriginalProtobufInputEvent.WaitCondition);
    public void SetCondition(WaitCondition condition)
    {
        WaitValidation.Validate(condition);
        if (condition.Equals(OriginalProtobufInputEvent.WaitCondition)) return;
        RestoreCondition(condition);
    }
    internal void RestoreCondition(WaitCondition condition)
    {
        OriginalProtobufInputEvent.WaitCondition = condition.Clone();
        OnPropertyChanged(nameof(Condition));
        OnPropertyChanged(nameof(Description));
    }
}
