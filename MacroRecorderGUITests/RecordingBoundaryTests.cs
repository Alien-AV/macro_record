using MacroRecorderGUI.Models;
using ProtobufGenerated;
using RecordPlaybackDLLEnums;
using static MacroRecorderGUITests.HotkeyChordTests;

namespace MacroRecorderGUITests;

[TestClass]
public class RecordingBoundaryTests
{
    [TestMethod]
    public void CtrlQStartDoesNotPublishCommandChordRemainder()
    {
        var transport = new FakeRecordingTransport();
        var capture = new RecordingCapture(transport);
        var session = new RecordingSession(fromHotkey: true);
        var received = new List<ProtobufInputEvent>();
        capture.Input += (_, input) => received.Add(input);
        capture.Start(session);
        transport.Begin(session, RecordingStartKeys.Q | RecordingStartKeys.Control);

        transport.Push(session, Key(0x51, false, 10));
        transport.Push(session, Key(0x51, true, 20));
        transport.Push(session, Key(0x11, true, 30));
        transport.Push(session, Key(0x41, false, 40));

        Assert.HasCount(1, received);
        Assert.AreEqual(0x41u, received[0].KeyboardEvent.VirtualKeyCode);
        Assert.AreEqual(100ul, received[0].TimeSinceLastEvent);
    }

    [TestMethod]
    public void DelayedOldSessionCallbacksCannotDrainNextSessionsChord()
    {
        var transport = new FakeRecordingTransport();
        var capture = new RecordingCapture(transport);
        var first = new RecordingSession(fromHotkey: true);
        var second = new RecordingSession(fromHotkey: true);
        var received = new List<(RecordingSession Session, ProtobufInputEvent Input)>();
        capture.Input += (session, input) => received.Add((session, input));
        capture.Start(first);
        capture.Stop();
        capture.Start(second);

        // Both UI commands completed before the old collector batch arrives.
        transport.Begin(first, RecordingStartKeys.Q | RecordingStartKeys.LeftControl);
        transport.Push(first, Key(0x51, true, 10));
        transport.Push(first, Key(0xA2, true, 20));
        transport.Push(first, Key(0x41, false, 30));
        Assert.IsFalse(first.Completion.IsCompleted);
        transport.End(first);
        Assert.IsTrue(first.Completion.IsCompletedSuccessfully);
        Assert.IsTrue(capture.IsRecording, "An old stop must not switch off the newer session.");

        transport.Begin(second, RecordingStartKeys.Q | RecordingStartKeys.RightControl);
        transport.Push(second, Key(0x51, false, 40));
        transport.Push(second, Key(0xA3, true, 50));
        transport.Push(second, Key(0x51, true, 60));
        transport.Push(second, Key(0x51, false, 70));
        transport.Push(first, Key(0x42, false));

        Assert.HasCount(2, received);
        Assert.AreSame(first, received[0].Session);
        Assert.AreEqual(60ul, received[0].Input.TimeSinceLastEvent);
        Assert.AreSame(second, received[1].Session);
        Assert.AreEqual(220ul, received[1].Input.TimeSinceLastEvent);
    }

    [TestMethod]
    public void OrdinaryStartRetainsTailUntilOrderedStopAndLeavesAllChordEventsIntact()
    {
        var transport = new FakeRecordingTransport();
        var capture = new RecordingCapture(transport);
        var session = new RecordingSession();
        var received = new List<ProtobufInputEvent>();
        capture.Input += (_, input) => received.Add(input);
        capture.Start(session);
        transport.Begin(session, RecordingStartKeys.Q | RecordingStartKeys.Control);
        capture.Stop();
        var tail = new[] { Mouse(0), Key(0x51, false, 10), Key(0x51, true, 20), Key(0x11, true, 30) };
        foreach (var input in tail)
        {
            transport.Push(session, input);
        }

        Assert.IsFalse(session.Completion.IsCompleted);
        transport.End(session);
        Assert.IsTrue(session.Completion.IsCompletedSuccessfully);
        CollectionAssert.AreEqual(tail, received);
    }

    [TestMethod]
    public void RepeatStartDoesNotResetFilterOrIssueAnotherNativeCommand()
    {
        var transport = new FakeRecordingTransport();
        var capture = new RecordingCapture(transport);
        var session = new RecordingSession(fromHotkey: true);
        var received = new List<ProtobufInputEvent>();
        capture.Input += (_, input) => received.Add(input);
        Assert.IsTrue(capture.Start(session));
        transport.Begin(session, RecordingStartKeys.Q);
        transport.Push(session, Key(0x51, true));
        Assert.IsFalse(capture.Start(new RecordingSession(fromHotkey: true)));
        transport.Push(session, Key(0x51, false));
        Assert.HasCount(1, transport.Starts);
        Assert.HasCount(1, received);
    }

    [TestMethod]
    public void EventsRequireAnExplicitStartBoundaryNotAMousePattern()
    {
        var transport = new FakeRecordingTransport();
        var capture = new RecordingCapture(transport);
        var session = new RecordingSession(fromHotkey: true);
        var received = new List<ProtobufInputEvent>();
        capture.Input += (_, input) => received.Add(input);
        capture.Start(session);
        transport.Push(session, Mouse(0));
        Assert.IsEmpty(received);
        transport.Begin(session, RecordingStartKeys.None);
        transport.Push(session, Mouse(0));
        transport.Push(session, Key(0x51, false));
        Assert.HasCount(2, received);
    }

    [TestMethod]
    public void DeferredDeliveryRetainsOriginalSessionContext()
    {
        var transport = new FakeRecordingTransport();
        var capture = new RecordingCapture(transport);
        var queuedUi = new Queue<Action>();
        var destination = new List<ProtobufInputEvent>();
        var otherDestination = new List<ProtobufInputEvent>();
        var first = new RecordingSession(context: destination);
        var second = new RecordingSession(context: otherDestination);
        capture.Input += (session, input) => queuedUi.Enqueue(() => ((List<ProtobufInputEvent>)session.Context!).Add(input));
        capture.Start(first);
        transport.Begin(first);
        transport.Push(first, Key(0x41, false));
        capture.Stop();
        capture.Start(second);
        transport.Push(first, Key(0x41, true));
        transport.End(first);
        transport.Begin(second);
        transport.Push(second, Key(0x42, false));
        while (queuedUi.TryDequeue(out var action))
        {
            action();
        }

        Assert.HasCount(2, destination);
        Assert.HasCount(1, otherDestination);
    }

    [TestMethod]
    public async Task FailedBoundaryCompletesItsSessionWithoutStoppingNewerSession()
    {
        var transport = new FakeRecordingTransport();
        var capture = new RecordingCapture(transport);
        var first = new RecordingSession();
        var second = new RecordingSession();
        capture.Start(first);
        capture.Stop();
        capture.Start(second);
        transport.Fail(first);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => first.Completion);
        Assert.IsTrue(capture.IsRecording);
        Assert.IsFalse(second.Completion.IsCompleted);
    }
}

internal sealed class FakeRecordingTransport : IRecordingTransport
{
    public event Action<ulong, ProtobufInputEvent>? Input;
    public event Action<ulong, RecordingBoundary, RecordingStartKeys, RecordingStartKeys>? Boundary;
    public event Action<StatusCode>? Status;
    public void PushStatus(StatusCode status) => Status?.Invoke(status);
    public void Dispose() { Disposed = true; OnDispose?.Invoke(); }
    public bool Disposed { get; private set; }
    public Action? OnDispose { get; set; }
    public Exception? StartError { get; set; }
    public Exception? StopError { get; set; }
    public List<ulong> Starts { get; } = [];
    public List<ulong> Stops { get; } = [];
    public List<RecordingStopGestures> StartGestures { get; } = [];
    public List<RecordingStopCommand?> StopCommands { get; } = [];
    public void Start(ulong sessionId, RecordingStopGestures stopGestures) { if (StartError is not null) throw StartError; Starts.Add(sessionId); StartGestures.Add(stopGestures); }
    public void Stop(ulong sessionId, RecordingStopCommand? command) { if (StopError is not null) throw StopError; Stops.Add(sessionId); StopCommands.Add(command); }
    public void Begin(RecordingSession session, RecordingStartKeys keys = RecordingStartKeys.None, RecordingStartKeys idleReleasedKeys = RecordingStartKeys.None) =>
        Begin(session.Id, keys, idleReleasedKeys);
    public void End(RecordingSession session) => End(session.Id);
    public void Fail(RecordingSession session) => Fail(session.Id);
    public void Push(RecordingSession session, ProtobufInputEvent input) => Input?.Invoke(session.Id, input);
    public void Begin(ulong id, RecordingStartKeys keys = RecordingStartKeys.None, RecordingStartKeys idleReleasedKeys = RecordingStartKeys.None) => Boundary?.Invoke(id, RecordingBoundary.Started, keys, idleReleasedKeys);
    public void End(ulong id) => Boundary?.Invoke(id, RecordingBoundary.Stopped, RecordingStartKeys.None, RecordingStartKeys.None);
    public void Fail(ulong id) => Boundary?.Invoke(id, RecordingBoundary.Failed, RecordingStartKeys.None, RecordingStartKeys.None);
    public void Push(ulong id, ProtobufInputEvent input) => Input?.Invoke(id, input);
}
