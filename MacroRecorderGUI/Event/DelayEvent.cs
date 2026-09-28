using ProtobufGenerated;

namespace MacroRecorderGUI.Event;

/// <summary>An explicit, cancellable pause which leaves held input unchanged.</summary>
public sealed class DelayEvent : InputEvent
{
    public const ulong MaximumDurationMicroseconds = 86_400_000_000;

    public DelayEvent(ulong durationMicroseconds) : this(new ProtobufInputEvent
        { Delay = new FixedDelay { DurationMicroseconds = durationMicroseconds } }) { }

    public DelayEvent(ProtobufInputEvent input)
    {
        if (input.Delay is null) throw new ArgumentException("A fixed delay payload is required.");
        ValidateDuration(input.Delay.DurationMicroseconds);
        OriginalProtobufInputEvent = input;
    }

    public ulong DurationMicroseconds
    {
        get => OriginalProtobufInputEvent.Delay.DurationMicroseconds;
        set
        {
            ValidateDuration(value);
            if (value == DurationMicroseconds) return;
            OriginalProtobufInputEvent.Delay.DurationMicroseconds = value;
            OnPropertyChanged();
        }
    }

    public static void ValidateDuration(ulong value)
    {
        if (value > MaximumDurationMicroseconds)
            throw new ArgumentException("Fixed delay must be between zero and 24 hours.");
    }
}
