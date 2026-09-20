using Google.Protobuf;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;
using Windows.System;
using static MacroRecorderGUITests.HotkeyChordTests;

namespace MacroRecorderGUITests;

[TestClass]
public class RecordingStopCommandTests
{
    [TestMethod]
    public void ActualHotkeyProvenanceAndOriginalMessageTimeReachNativeStop()
    {
        var transport = new FakeRecordingTransport();
        using var capture = new RecordingCapture(transport);
        var session = new RecordingSession(stopGestures: RecordingStopGestures.ControlW | RecordingStopGestures.ControlAltF12);
        capture.Start(session);
        transport.Begin(session);
        var command = new RecordingStopCommand(RecordingStopGestures.ControlAltF12, session.RequestedAt + 12);
        Assert.IsTrue(capture.Stop(command));
        Assert.AreEqual(session.StopGestures, transport.StartGestures.Single());
        Assert.AreEqual(command, transport.StopCommands.Single());
        Assert.IsFalse(session.Completion.IsCompleted);
        transport.End(session);
        Assert.IsTrue(session.Completion.IsCompletedSuccessfully);
    }

    [TestMethod]
    public void OrdinaryStopAndQueuedTailPreserveUnknownFieldsAndExactDelays()
    {
        var transport = new FakeRecordingTransport();
        using var capture = new RecordingCapture(transport);
        var session = new RecordingSession(stopGestures: RecordingStopGestures.ControlW);
        var received = new List<ProtobufInputEvent>();
        capture.Input += (_, input) => received.Add(input);
        capture.Start(session);
        transport.Begin(session);
        var input = ProtobufInputEvent.Parser.ParseFrom(Key(0x11, false, 123456).ToByteArray().Concat(new byte[] { 0xF8, 0x07, 0x7B }).ToArray());
        var original = input.ToByteArray();
        Assert.IsTrue(capture.Stop());
        transport.Push(session, input);
        transport.End(session);
        Assert.IsNull(transport.StopCommands.Single());
        Assert.AreSame(input, received.Single());
        CollectionAssert.AreEqual(original, received.Single().ToByteArray());
    }

    [TestMethod]
    public void OldOrUnregisteredHotkeyCannotStopNewSession()
    {
        var transport = new FakeRecordingTransport();
        using var capture = new RecordingCapture(transport);
        var session = new RecordingSession(stopGestures: RecordingStopGestures.ControlW);
        capture.Start(session);
        Assert.IsFalse(capture.Stop(new(RecordingStopGestures.ControlW, unchecked(session.RequestedAt - 1))));
        Assert.IsFalse(capture.Stop(new(RecordingStopGestures.ControlR, session.RequestedAt)));
        Assert.IsFalse(capture.Stop(new(RecordingStopGestures.None, session.RequestedAt)));
        Assert.IsTrue(capture.IsRecording);
        Assert.IsEmpty(transport.Stops);
        Assert.IsTrue(capture.Stop(new(RecordingStopGestures.ControlW, session.RequestedAt)));
    }

    [TestMethod]
    public void OldBoundaryCannotChangeNewSessionsStopConfiguration()
    {
        var transport = new FakeRecordingTransport();
        using var capture = new RecordingCapture(transport);
        var first = new RecordingSession(stopGestures: RecordingStopGestures.ControlW);
        var second = new RecordingSession(stopGestures: RecordingStopGestures.ControlShiftF12);
        capture.Start(first);
        capture.Stop(new(RecordingStopGestures.ControlW, first.RequestedAt));
        capture.Start(second);
        transport.Begin(first);
        transport.End(first);
        Assert.IsTrue(capture.IsRecording);
        Assert.IsFalse(capture.Stop(new(RecordingStopGestures.ControlW, second.RequestedAt)));
        Assert.IsTrue(capture.Stop(new(RecordingStopGestures.ControlShiftF12, second.RequestedAt)));
        CollectionAssert.AreEqual(new[] { RecordingStopGestures.ControlW, RecordingStopGestures.ControlShiftF12 }, transport.StartGestures);
    }

    [TestMethod]
    public void ConfigurationSurvivesContentRolloverWithoutRearmingStartChord()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredViewModel(transport) { RegisteredRecordingStops = RecordingStopGestures.ControlW | RecordingStopGestures.ControlR };
        vm.StartRecording(fromHotkey: true);
        var first = transport.Starts.Single();
        transport.Begin(first, RecordingStartKeys.Q | RecordingStartKeys.Control);
        transport.Push(first, Key(0x51, true, 10));
        transport.Push(first, Key(0x11, true, 20));
        vm.RegisteredRecordingStops = RecordingStopGestures.ControlAltF12;
        vm.ActiveMacro!.Clear();
        var second = transport.Starts.Last();
        transport.End(first);
        transport.Begin(second);
        transport.Push(second, Key(0x11, false, 30));
        vm.Deliver();
        CollectionAssert.AreEqual(new[] { RecordingStopGestures.ControlW | RecordingStopGestures.ControlR,
            RecordingStopGestures.ControlW | RecordingStopGestures.ControlR }, transport.StartGestures);
        Assert.IsNull(transport.StopCommands.Single());
        Assert.AreEqual(30ul, vm.ActiveMacro.Events.Single().TimeSinceLastEvent);
    }

    [TestMethod]
    public async Task HotkeyStopStillWaitsForNativeAndDeferredUiDrain()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredViewModel(transport) { RegisteredRecordingStops = RecordingStopGestures.ControlW };
        vm.StartRecording();
        var id = transport.Starts.Single();
        transport.Begin(id);
        var command = new RecordingStopCommand(RecordingStopGestures.ControlW, unchecked((uint)Environment.TickCount));
        var stopped = vm.StopRecordingAsync(command: command);
        transport.Push(id, Key(0x41, false, 55));
        Assert.IsFalse(stopped.IsCompleted);
        transport.End(id);
        Assert.IsFalse(stopped.IsCompleted);
        vm.Deliver();
        await stopped;
        Assert.AreEqual(command, transport.StopCommands.Single());
        Assert.AreEqual(55ul, vm.ActiveMacro!.Events.Single().TimeSinceLastEvent);
    }

    [TestMethod]
    public void SuccessfulAndFailedRegistrationsControlOnlyEffectiveGestures()
    {
        var registered = new HashSet<int>();
        var reject = false;
        using var hotkeys = new GlobalHotkeys((id, _, _) => !reject && registered.Add(id), registered.Remove);
        var commands = new List<RecordingStopCommand>();
        reject = true;
        Assert.IsFalse(hotkeys.AddHotKey(VirtualKey.W, HotKeyModifiers.Control, _ => { }));
        Assert.AreEqual(RecordingStopGestures.None, hotkeys.RegisteredRecordingStops);
        reject = false;
        Assert.IsTrue(hotkeys.AddHotKey(VirtualKey.W, HotKeyModifiers.Control, _ => { }));
        Assert.IsTrue(hotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[0], commands.Add, out _));
        var originalId = registered.Max();
        reject = true;
        Assert.IsFalse(hotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[1], commands.Add, out _));
        Assert.AreEqual(RecordingStopGestures.ControlW | RecordingStopGestures.ControlR, hotkeys.RegisteredRecordingStops);
        Assert.IsTrue(hotkeys.DispatchHotkey(originalId, 0xfffffffe));
        Assert.AreEqual(new RecordingStopCommand(RecordingStopGestures.ControlR, 0xfffffffe), commands.Single());
        reject = false;
        Assert.IsTrue(hotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[1], commands.Add, out _));
        Assert.IsFalse(hotkeys.DispatchHotkey(originalId, 1));
        Assert.AreEqual(RecordingStopGestures.ControlW | RecordingStopGestures.ControlAltF12, hotkeys.RegisteredRecordingStops);
        Assert.IsTrue(hotkeys.DispatchHotkey(registered.Max(), 1));
        Assert.AreEqual(new RecordingStopCommand(RecordingStopGestures.ControlAltF12, 1), commands.Last());
        hotkeys.Dispose();
        Assert.AreEqual(RecordingStopGestures.None, hotkeys.RegisteredRecordingStops);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FailedReplacementRetainsExactlyThoseHandlersStillRegistered(bool rollbackAlsoFails)
    {
        var registered = new HashSet<int>();
        var failRelease = false;
        using var hotkeys = new GlobalHotkeys((id, _, _) => registered.Add(id),
            id => !(failRelease && (id == 9000 || rollbackAlsoFails)) && registered.Remove(id));
        var commands = new List<RecordingStopCommand>();
        hotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[0], commands.Add, out _);
        failRelease = true;
        Assert.IsFalse(hotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[2], commands.Add, out _));
        Assert.AreEqual(RecordingStopGestures.ControlR | (rollbackAlsoFails ? RecordingStopGestures.ControlShiftF12 : 0), hotkeys.RegisteredRecordingStops);
        Assert.AreEqual(KeyboardShortcuts.EmergencyStopChoices[0], hotkeys.EmergencyStop);
        Assert.AreEqual(rollbackAlsoFails, hotkeys.DispatchHotkey(9001, 77));
        Assert.IsTrue(hotkeys.DispatchHotkey(9000, 88));
        Assert.AreEqual(new RecordingStopCommand(RecordingStopGestures.ControlR, 88), commands.Last());
        failRelease = false;
    }

    private sealed class DeferredViewModel(FakeRecordingTransport transport)
        : MainWindowViewModel(new RecordEngine(transport), new FakePlaybackEngine())
    {
        private readonly Queue<Action> _pending = [];
        protected override void InvokeDispatcher(Action action) => _pending.Enqueue(action);
        public void Deliver() { while (_pending.TryDequeue(out var action)) action(); }
    }
}
