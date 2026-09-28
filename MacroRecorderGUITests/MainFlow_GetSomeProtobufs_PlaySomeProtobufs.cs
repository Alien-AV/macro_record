using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

internal sealed class FakeRecordEngine : IRecordEngine
{
    private readonly FakeRecordingTransport _transport = new();
    private readonly RecordEngine _engine;
    private RecordingSession? _session;
    public FakeRecordEngine() => _engine = new RecordEngine(_transport);
    public event RecordEngine.RecordEventsEventHandler? RecordedEvent { add => _engine.RecordedEvent += value; remove => _engine.RecordedEvent -= value; }
    public event RecordEngine.RecordStatusEventHandler? RecordStatus { add => _engine.RecordStatus += value; remove => _engine.RecordStatus -= value; }
    public event Action<RecordingSession, Exception?>? RecordingEnded { add => _engine.RecordingEnded += value; remove => _engine.RecordingEnded -= value; }
    public event Action<RecordingSession, PointerPosition?>? RecordingStarted { add => _engine.RecordingStarted += value; remove => _engine.RecordingStarted -= value; }
    public void PushStatus(RecordPlaybackDLLEnums.StatusCode status) => _transport.PushStatus(status);
    public bool StartRecord(RecordingSession session)
    {
        if (!_engine.StartRecord(session)) return false;
        _session = session;
        _transport.Begin(session, origin: new(0, 0));
        return true;
    }

    public bool StopRecord(RecordingStopCommand? command = null)
    {
        if (!_engine.StopRecord(command)) return false;
        if (_session is { } session) _transport.End(session);
        _session = null;
        return true;
    }
    public void Dispose() => _engine.Dispose();
    public CapturedWaitSubmission CapturedWait(WaitCondition? condition, RecordingCaptureGesture gesture, uint messageTime) => _engine.CapturedWait(condition, gesture, messageTime);

    public void PushEvent(ProtobufInputEvent fakeEvent)
    {
        _transport.Push(_session ?? throw new InvalidOperationException("Start a recording session before supplying input."), fakeEvent);
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
    public int LoopUpdates { get; private set; }
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
    public void SetLoopPlayback(bool loop) { Loop = loop; LoopUpdates++; }
}

internal sealed class FakeMainWindowViewModel : MainWindowViewModel
{
    public FakeMainWindowViewModel(IRecordEngine recordEngine, IPlaybackEngine playbackEngine)
        : base(recordEngine, playbackEngine, null, new FakePointerEnvironment())
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
        viewModel.StartRecording();

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

        await viewModel.StopRecordingAsync();
        await viewModel.ActiveMacro!.PlayMacro();
        Assert.AreEqual(new PointerPosition(0, 0), viewModel.ActiveMacro.PointerOrigins.Single().Position);
        var actualPlaybackEvents = playbackEngine.PlayedEvents.Skip(1)
            .Select(inputEvent => inputEvent.OriginalProtobufInputEvent)
            .ToList();

        CollectionAssert.AreEqual(expectedEvents, actualPlaybackEvents);
    }
}
