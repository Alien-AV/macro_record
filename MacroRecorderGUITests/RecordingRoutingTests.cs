using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using static MacroRecorderGUITests.HotkeyChordTests;

namespace MacroRecorderGUITests;

[TestClass]
public class RecordingRoutingTests
{
    [TestMethod]
    public void StopAdjustsOriginalTargetAfterItsDeferredTailAndBeforeNewSessionInput()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredRecordingViewModel(transport);
        var first = vm.ActiveMacro!;
        first.AddEvent(InputEvent.CreateInputEvent(Key(0x41, true, 900)));
        vm.StartRecording(fromHotkey: true);
        var id = transport.Starts.Single();
        transport.Begin(id, RecordingStartKeys.Q | RecordingStartKeys.LeftControl);
        transport.Push(id, Key(0x51, true, 10));
        transport.Push(id, Key(0x41, false, 20));
        var second = vm.AddNewTab();
        vm.StopRecording(autoDelay: 777);
        transport.Push(id, Key(0xA2, true, 30));
        transport.Push(id, Key(0x41, true, 40));
        transport.End(id);
        Assert.HasCount(1, first.Events, "Native completion must not be mistaken for UI completion.");
        Assert.AreEqual(900ul, first.Events[0].TimeSinceLastEvent);
        vm.StartRecording();
        var next = transport.Starts.Last();
        transport.Begin(next);
        transport.Push(next, Key(0x42, false, 888));
        vm.Deliver();
        Assert.HasCount(3, first.Events);
        Assert.IsTrue(first.Events.All(input => input.TimeSinceLastEvent == 777));
        Assert.HasCount(1, second.Events);
        Assert.AreEqual(888ul, second.Events[0].TimeSinceLastEvent);
    }

    [TestMethod]
    public void RapidStartOnSameMacroDoesNotApplyEarlierAutoDelayToNewInput()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredRecordingViewModel(transport);
        vm.StartRecording();
        var first = transport.Starts.Last();
        transport.Begin(first);
        transport.Push(first, Key(0x41, false, 20));
        vm.StopRecording(123);
        vm.StartRecording();
        var second = transport.Starts.Last();
        transport.Push(first, Key(0x41, true, 30));
        transport.End(first);
        transport.Begin(second);
        transport.Push(second, Key(0x42, false, 40));
        vm.Deliver();
        CollectionAssert.AreEqual(new ulong[] { 123, 123, 40 }, vm.ActiveMacro!.Events.Select(input => input.TimeSinceLastEvent).ToArray());
    }

    [TestMethod]
    [DataRow("clear")]
    [DataRow("replace")]
    [DataRow("close")]
    [DataRow("new-clear-session")]
    public void ExplicitContentChangesInvalidateAlreadyQueuedAndLateNativeInput(string change)
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredRecordingViewModel(transport);
        var original = vm.ActiveMacro!;
        vm.StartRecording();
        var first = transport.Starts.Last();
        transport.Begin(first);
        transport.Push(first, Key(0x41, false));
        vm.StopRecording(123);
        var replacement = InputEvent.CreateInputEvent(Key(0x42, false, 456));
        switch (change)
        {
            case "clear": original.Clear(); break;
            case "replace": original.PopulateEventCollectionWithNewEvents([replacement]); break;
            case "close": vm.CloseTab(original); vm.AddNewTab(); break;
            default:
                vm.StartRecording(clear: true);
                transport.Begin(transport.Starts.Last());
                transport.Push(transport.Starts.Last(), Key(0x42, false, 456));
                break;
        }
        transport.Push(first, Key(0x41, true));
        transport.End(first);
        vm.Deliver();
        Assert.HasCount(change is "replace" or "new-clear-session" ? 1 : 0, original.Events);
        Assert.IsFalse(original.Events.OfType<KeyboardEvent>().Any(input => input.VirtualKeyCode == 0x41));
        if (original.Events.Count != 0) Assert.AreEqual(456ul, original.Events[0].TimeSinceLastEvent);
    }

    [TestMethod]
    public void RepeatStartDoesNotClearAnActiveMacroOrResetTheChord()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredRecordingViewModel(transport);
        vm.ActiveMacro!.AddEvent(InputEvent.CreateInputEvent(Key(0x41, false)));
        Assert.IsTrue(vm.StartRecording(fromHotkey: true));
        Assert.IsFalse(vm.StartRecording(fromHotkey: true, clear: true));
        Assert.HasCount(1, vm.ActiveMacro.Events);
        Assert.HasCount(1, transport.Starts);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ClearOrReplaceDuringCaptureContinuesFreshInputWithoutRearmingShortcutFilter(bool replace)
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredRecordingViewModel(transport);
        vm.StartRecording(fromHotkey: true);
        var old = transport.Starts.Single();
        transport.Begin(old, RecordingStartKeys.Q | RecordingStartKeys.LeftControl);
        transport.Push(old, Key(0x51, true));
        transport.Push(old, Key(0xA2, true));
        transport.Push(old, Key(0x41, false));
        var macro = vm.ActiveMacro!;
        if (replace) macro.PopulateEventCollectionWithNewEvents([InputEvent.CreateInputEvent(Key(0x43, false))]);
        else macro.Clear();
        var current = transport.Starts.Last();
        Assert.AreNotEqual(old, current);
        CollectionAssert.AreEqual(new[] { old }, transport.Stops);
        transport.Push(old, Key(0x41, true));
        transport.End(old);
        // A fresh Q press at rollover is ordinary typing, even though it is held.
        transport.Begin(current, RecordingStartKeys.Q);
        transport.Push(current, Key(0x51, false, 20));
        transport.Push(current, Key(0x51, true, 30));
        vm.Deliver();
        Assert.HasCount(replace ? 3 : 2, macro.Events);
        Assert.IsFalse(macro.Events.OfType<KeyboardEvent>().Any(input => input.VirtualKeyCode == 0x41));
        CollectionAssert.AreEqual(new ulong[] { 20, 30 }, macro.Events.TakeLast(2).Select(input => input.TimeSinceLastEvent).ToArray());
    }

    [TestMethod]
    public void ClearBeforeOriginalReadinessKeepsCommandDrainAndDoesNotCarryDiscardedDelay()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredRecordingViewModel(transport);
        vm.StartRecording(fromHotkey: true);
        var old = transport.Starts.Single();
        vm.ActiveMacro!.Clear();
        var current = transport.Starts.Last();
        transport.Begin(old, RecordingStartKeys.Q | RecordingStartKeys.LeftControl);
        transport.Push(old, Key(0x51, false, 100));
        transport.End(old);
        transport.Begin(current, RecordingStartKeys.Q | RecordingStartKeys.LeftControl);
        transport.Push(current, Key(0x51, false, 10));
        transport.Push(current, Key(0x51, true, 20));
        transport.Push(current, Key(0xA2, true, 30));
        transport.Push(current, Key(0x41, false, 40));
        vm.Deliver();
        Assert.HasCount(1, vm.ActiveMacro.Events);
        Assert.AreEqual(100ul, vm.ActiveMacro.Events[0].TimeSinceLastEvent);
    }

    [TestMethod]
    public void ClosingCaptureTargetStopsItsSessionAndDoesNotRouteTailIntoAnotherTab()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredRecordingViewModel(transport);
        var owner = vm.ActiveMacro!;
        vm.StartRecording();
        var id = transport.Starts.Single();
        transport.Begin(id);
        var other = vm.AddNewTab();
        vm.CloseTab(owner);
        CollectionAssert.AreEqual(new[] { id }, transport.Stops);
        transport.Push(id, Key(0x41, false));
        transport.End(id);
        vm.Deliver();
        Assert.IsEmpty(other.Events);
        Assert.IsEmpty(owner.Events);
    }

    [TestMethod]
    public void HotkeyUnknownInitialReleaseIsSuppressedButLaterDownAndUpSurvive()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredRecordingViewModel(transport);
        vm.StartRecording(fromHotkey: true);
        var id = transport.Starts.Single();
        transport.Begin(id);
        transport.Push(id, Key(0xA2, true, 5));
        transport.Push(id, Key(0x51, true, 7));
        transport.Push(id, Key(0x51, false, 11));
        transport.Push(id, Key(0x51, true, 13));
        vm.Deliver();
        CollectionAssert.AreEqual(new ulong[] { 23, 13 }, vm.ActiveMacro!.Events.Select(input => input.TimeSinceLastEvent).ToArray());
    }

    [TestMethod]
    public void ReadinessFailureIsReportedAndNextCaptureCanStart()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredRecordingViewModel(transport);
        var messages = new List<string>();
        vm.StatusMessageRequested += (_, message) => messages.Add(message);
        vm.StartRecording();
        transport.Fail(transport.Starts.Single());
        vm.Deliver();
        StringAssert.Contains(messages.Last(), "Could not record");
        Assert.IsTrue(vm.StartRecording());
    }

    [TestMethod]
    public void FailedStopKeepsSessionAvailableForRetryAndDoesNotApplyAutoDelay()
    {
        var transport = new FakeRecordingTransport { StopError = new InvalidOperationException("fake stop failure") };
        using var vm = new DeferredRecordingViewModel(transport);
        vm.StartRecording();
        var id = transport.Starts.Single();
        transport.Begin(id);
        vm.StopRecording(123);
        Assert.IsFalse(vm.StartRecording());
        transport.Push(id, Key(0x41, false, 456));
        transport.StopError = null;
        vm.StopRecording();
        transport.End(id);
        vm.Deliver();
        Assert.AreEqual(456ul, vm.ActiveMacro!.Events.Single().TimeSinceLastEvent);
    }

    [TestMethod]
    public async Task DisposalAllowsFinalCallbacksWithoutHoldingTheSessionLockAcrossJoin()
    {
        var transport = new FakeRecordingTransport();
        var engine = new RecordEngine(transport);
        var session = new RecordingSession();
        engine.StartRecord(session);
        transport.Begin(session);
        transport.OnDispose = () => Task.Run(() => transport.End(session)).Wait(TimeSpan.FromSeconds(2));
        await Task.Run(engine.Dispose).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(session.Completion.IsCompletedSuccessfully);
        Assert.IsTrue(transport.Disposed);
    }

    private sealed class DeferredRecordingViewModel(FakeRecordingTransport transport)
        : MainWindowViewModel(new RecordEngine(transport), new FakePlaybackEngine())
    {
        private readonly Queue<Action> _pending = [];
        protected override void InvokeDispatcher(Action action) => _pending.Enqueue(action);
        public void Deliver() { while (_pending.TryDequeue(out var action)) action(); }
    }
}
