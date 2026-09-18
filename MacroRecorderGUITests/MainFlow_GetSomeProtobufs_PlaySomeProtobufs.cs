using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

internal sealed class FakeRecordEngine : IRecordEngine
{
    public event RecordEngine.RecordEventsEventHandler? RecordedEvent;
    public event RecordEngine.RecordStatusEventHandler? RecordStatus;

    public void PushStatus(RecordPlaybackDLLEnums.StatusCode status) =>
        RecordStatus?.Invoke(this, new RecordEngine.RecordStatusEventArgs(status));

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

    public bool Loop { get; private set; }
    public int Starts { get; private set; }
    public int Aborts { get; private set; }
    public bool Disposed { get; private set; }
    public TaskCompletionSource? Pending { get; set; }

    public Task PlaybackEventsAsync(IEnumerable<InputEvent> events, bool loop = false)
    {
        PlayedEvents = events.ToArray();
        Loop = loop;
        Starts++;
        return Pending?.Task ?? Task.CompletedTask;
    }

    public void PlaybackEventAbort()
    {
        Aborts++;
    }

    public void Dispose() => Disposed = true;
    public void SetLoopPlayback(bool loop) => Loop = loop;
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
    public async Task RecordedProtobufEventsArePassedToPlaybackWithoutSyntheticModifierReleases()
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

        await viewModel.ActiveMacro!.PlayMacro();
        var actualPlaybackEvents = playbackEngine.PlayedEvents
            .Select(inputEvent => inputEvent.OriginalProtobufInputEvent)
            .ToList();

        CollectionAssert.AreEqual(expectedEvents, actualPlaybackEvents);
    }
}
