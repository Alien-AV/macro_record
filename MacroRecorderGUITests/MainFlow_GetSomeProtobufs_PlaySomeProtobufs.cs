using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

internal sealed class FakeRecordEngine : IRecordEngine
{
    public event RecordEngine.RecordEventsEventHandler? RecordedEvent;
    public event RecordEngine.RecordStatusEventHandler? RecordStatus
    {
        add { }
        remove { }
    }

    public void StartRecord()
    {
    }

    public void StopRecord()
    {
    }

    public void PushEvent(ProtobufInputEvent fakeEvent)
    {
        RecordedEvent?.Invoke(this, new RecordEngine.RecordEventsEventArgs(fakeEvent));
    }

    public static ProtobufInputEvent MakeKeyboardEvent(
        uint virtualKey,
        bool keyUp,
        ulong timeSinceLastEvent)
    {
        return new ProtobufInputEvent
        {
            KeyboardEvent = new ProtobufInputEvent.Types.KeyboardEventType
            {
                KeyUp = keyUp,
                VirtualKeyCode = virtualKey
            },
            TimeSinceLastEvent = timeSinceLastEvent
        };
    }

    public static ProtobufInputEvent MakeMouseEvent(
        int x,
        int y,
        uint actionType,
        bool relativePosition,
        ulong timeSinceLastEvent)
    {
        return new ProtobufInputEvent
        {
            MouseEvent = new ProtobufInputEvent.Types.MouseEventType
            {
                ActionType = actionType,
                MappedToVirtualDesktop = false,
                RelativePosition = relativePosition,
                WheelRotation = 0,
                X = x,
                Y = y
            },
            TimeSinceLastEvent = timeSinceLastEvent
        };
    }
}

internal sealed class FakePlaybackEngine : IPlaybackEngine
{
    public IEnumerable<InputEvent> PlayedEvents { get; private set; } = [];

    public void PlaybackEvents(IEnumerable<InputEvent> events)
    {
        PlayedEvents = events;
    }

    public void PlaybackEventAbort()
    {
    }
}

internal sealed class FakeMainWindowViewModel : MainWindowViewModel
{
    public FakeMainWindowViewModel(IRecordEngine recordEngine, IPlaybackEngine playbackEngine)
        : base(recordEngine, playbackEngine)
    {
    }

    protected override void InvokeDispatcher(Action action)
    {
        action();
    }
}

[TestClass]
public class MainFlowTest
{
    [TestMethod]
    public void RecordedProtobufEventsArePassedToPlaybackWithReleasedModifierKeys()
    {
        var recordEngine = new FakeRecordEngine();
        var playbackEngine = new FakePlaybackEngine();
        var viewModel = new FakeMainWindowViewModel(recordEngine, playbackEngine);

        var expectedEvents = new List<ProtobufInputEvent>
        {
            FakeRecordEngine.MakeKeyboardEvent(100, false, 1000),
            FakeRecordEngine.MakeKeyboardEvent(100, true, 500),
            FakeRecordEngine.MakeMouseEvent(100, 100, (uint)MouseActionTypeFlags.Move, false, 500),
            FakeRecordEngine.MakeMouseEvent(100, 100, (uint)MouseActionTypeFlags.LeftDown, false, 500),
            FakeRecordEngine.MakeMouseEvent(100, 100, (uint)MouseActionTypeFlags.LeftUp, false, 500)
        };

        foreach (var inputEvent in expectedEvents)
        {
            recordEngine.PushEvent(inputEvent);
        }

        viewModel.ActiveMacro!.PlayMacro();

        var releasedModifierEvents = ReleaseModifierKeys.ReleaseModKeysEvents
            .Select(inputEvent => inputEvent.OriginalProtobufInputEvent)
            .ToList();
        var expectedPlaybackEvents = releasedModifierEvents
            .Concat(expectedEvents)
            .Concat(releasedModifierEvents)
            .ToList();
        var actualPlaybackEvents = playbackEngine.PlayedEvents
            .Select(inputEvent => inputEvent.OriginalProtobufInputEvent)
            .ToList();

        CollectionAssert.AreEqual(expectedPlaybackEvents, actualPlaybackEvents);
    }
}
