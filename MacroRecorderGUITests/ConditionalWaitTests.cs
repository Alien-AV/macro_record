using Google.Protobuf;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class ConditionalWaitTests
{
    internal static WaitCondition Condition(WaitTrigger trigger = WaitTrigger.IsTrue)
    {
        var wait = WaitValidation.NewWindow();
        wait.Window.Target.Title = "Export";
        wait.Trigger = trigger;
        return wait;
    }
    private static WaitObservation Sample(bool match, string identity = "one", uint? value = null) =>
        new(match ? ObservationState.Match : ObservationState.NoMatch, "fake", identity, value ?? (match ? 1u : 0u), [identity], match ? [identity] : []);

    [TestMethod]
    public void DefaultAlreadyTrueRequiresFreshStableObservations()
    {
        var evaluator = new WaitEvaluator(Condition());
        Assert.IsFalse(evaluator.Sample(Sample(true), TimeSpan.Zero));
        Assert.IsFalse(evaluator.Sample(Sample(true), TimeSpan.FromMilliseconds(100)));
        Assert.IsTrue(evaluator.Sample(Sample(true), TimeSpan.FromMilliseconds(200)));
    }

    [TestMethod]
    public void FlickerUnavailableAndSamplingGapsResetStability()
    {
        foreach (var interrupt in new[] { Sample(false), new WaitObservation(ObservationState.Unavailable, "denied"), new WaitObservation(ObservationState.Error, "failed") })
        {
            var evaluator = new WaitEvaluator(Condition());
            evaluator.Sample(Sample(true), TimeSpan.Zero);
            Assert.IsFalse(evaluator.Sample(interrupt, TimeSpan.FromMilliseconds(100)));
            Assert.IsFalse(evaluator.Sample(Sample(true), TimeSpan.FromMilliseconds(200)));
            Assert.IsTrue(evaluator.Sample(Sample(true), TimeSpan.FromMilliseconds(400)));
            Assert.IsFalse(evaluator.Sample(Sample(true), TimeSpan.FromMilliseconds(1000)));
            Assert.IsFalse(evaluator.Sample(Sample(true), TimeSpan.FromMilliseconds(1100), fresh: false));
            Assert.IsFalse(evaluator.Sample(Sample(true), TimeSpan.FromMilliseconds(1200)));
        }
    }

    [TestMethod]
    public void BecomesTrueRequiresFalseAfterArmingAndUnavailableDoesNotArm()
    {
        var condition = Condition(WaitTrigger.BecomesTrue); condition.StableForUs = 0;
        var evaluator = new WaitEvaluator(condition);
        Assert.IsFalse(evaluator.Sample(Sample(true), TimeSpan.Zero));
        Assert.IsFalse(evaluator.Sample(new(ObservationState.Unavailable, "missing"), TimeSpan.FromMilliseconds(100)));
        Assert.IsFalse(evaluator.Sample(Sample(true), TimeSpan.FromMilliseconds(200)));
        Assert.IsFalse(evaluator.Sample(Sample(false), TimeSpan.FromMilliseconds(300)));
        Assert.IsTrue(evaluator.Sample(Sample(true), TimeSpan.FromMilliseconds(400)));
        Assert.IsFalse(new WaitEvaluator(condition).Sample(Sample(true), TimeSpan.Zero), "Each occurrence must rearm.");
    }

    [TestMethod]
    public void ChangesBindsIdentityAndNewWindowCapturesInitialInstanceSet()
    {
        var condition = Condition(WaitTrigger.Changes); condition.StableForUs = 0;
        var evaluator = new WaitEvaluator(condition);
        Assert.IsFalse(evaluator.Sample(Sample(false), TimeSpan.Zero));
        Assert.IsFalse(evaluator.Sample(Sample(true, "replacement-process"), TimeSpan.FromMilliseconds(100)));
        Assert.IsTrue(evaluator.Sample(Sample(true), TimeSpan.FromMilliseconds(200)));
        condition.Trigger = WaitTrigger.NewWindow;
        evaluator = new(condition);
        Assert.IsFalse(evaluator.Sample(Sample(true), TimeSpan.Zero));
        Assert.IsFalse(evaluator.Sample(Sample(true), TimeSpan.FromMilliseconds(100)));
        Assert.IsTrue(evaluator.Sample(Sample(true, "new-process"), TimeSpan.FromMilliseconds(200)));
    }

    [TestMethod]
    public void ValidationRejectsUnknownVersionsEnumsProvidersAndUnsafeBounds()
    {
        Action<WaitCondition>[] corruptions = [w => w.SemanticsVersion = 2, w => w.Trigger = (WaitTrigger)99,
            w => w.TimeoutUs = 0, w => w.TimeoutUs = ulong.MaxValue, w => w.StableForUs = w.TimeoutUs + 1,
            w => w.PollIntervalUs = 1, w => w.PollIntervalUs = ulong.MaxValue, w => w.ClearCondition(),
            w => w.Window.Test = (WindowTest)123, w => w.Window.Target.TitleMatch = (TitleMatch)99,
            w => w.Window.Target.Title = "", w => w.Window.Target.Title = "x\0x", w => w.Window.Target.ExecutablePath = "foo.exe"];
        foreach (var corrupt in corruptions) { var w = Condition(); corrupt(w); Assert.Throws<ArgumentException>(() => WaitValidation.Validate(w)); }
        WaitValidation.Validate(Condition());
    }

    [TestMethod]
    public void WaitsAreStandaloneAndInsertionEditUndoPreserveDelaysAndNestedUnknownFields()
    {
        using var macro = new MacroViewModel("Wait", new FakePlaybackEngine());
        macro.AddEvent(new KeyboardEvent(VirtualKey.A, false));
        macro.AddEvent(new KeyboardEvent(VirtualKey.A, true) { TimeSinceLastEvent = 15 });
        var condition = Condition();
        condition = WaitCondition.Parser.ParseFrom(condition.ToByteArray().Concat(new byte[] { 0xa0, 0x06, 0x07 }).ToArray());
        condition.Window.Target = WindowSelector.Parser.ParseFrom(condition.Window.Target.ToByteArray().Concat(new byte[] { 0xa0, 0x06, 0x08 }).ToArray());
        var editor = macro.Editor; editor.Refresh();
        var added = editor.InsertWait(editor.Projection.Actions[0], condition);
        editor.Refresh();
        Assert.AreEqual(ActionKind.Wait, editor.Projection.Actions[1].Kind);
        Assert.AreEqual(1, editor.Projection.Actions[1].Count);
        Assert.AreEqual(15UL, macro.Events[1].TimeSinceLastEvent);
        var changed = added.Condition; changed.TimeoutUs = 50_000_000;
        editor.SetCondition(editor.Projection.Actions[1], changed);
        Assert.IsTrue(editor.Undo());
        CollectionAssert.AreEqual(condition.ToByteArray(), added.Condition.ToByteArray());
        Assert.IsTrue(editor.Undo()); Assert.AreEqual(2, macro.Events.Count);
    }

    [TestMethod]
    public void ReplaceDelayIsExplicitAndHeldKeyOrMouseWaitIsRejectedWithoutMutation()
    {
        using var macro = new MacroViewModel("Wait", new FakePlaybackEngine());
        macro.AddEvent(new KeyboardEvent(VirtualKey.A, false) { TimeSinceLastEvent = 200 });
        macro.Editor.Refresh();
        Assert.Throws<ArgumentException>(() => macro.Editor.InsertWait(macro.Editor.Projection.Actions[0], Condition()));
        Assert.AreEqual(1, macro.Events.Count);
        var added = macro.Editor.ReplaceDelayWithWait(macro.Editor.Projection.Actions[0], Condition());
        Assert.AreSame(added, macro.Events[0]); Assert.AreEqual(0UL, macro.Events[1].TimeSinceLastEvent);
        Assert.IsTrue(macro.Editor.Undo()); Assert.AreEqual(200UL, macro.Events[0].TimeSinceLastEvent);
        var mouse = new MouseEvent(0, 0, MacroRecorderGUI.Common.MouseActionTypeFlags.LeftDown);
        Assert.Throws<ArgumentException>(() => WaitValidation.ValidateSchedule([mouse, new WaitConditionEvent(Condition())]));
        Assert.Throws<ArgumentException>(() => WaitValidation.ValidateSchedule([new WaitConditionEvent(Condition()), macro.Events[0]], true));
    }

    [TestMethod]
    public void ConsecutiveAndTrailingPreviewCheckpointsNeverCollapseAtSameTimestamp()
    {
        using var macro = new MacroViewModel("Wait", new FakePlaybackEngine());
        macro.AddEvent(new WaitConditionEvent(Condition())); macro.AddEvent(new WaitConditionEvent(Condition()));
        macro.Editor.Refresh(); var preview = new VisualPreview(macro.Events, macro.Editor.Projection);
        Assert.AreEqual(1, preview.Seek(0).Current!.Number);
        Assert.IsTrue(preview.SimulateSatisfied(0)); Assert.AreEqual(2, preview.Seek(0).Current!.Number);
        Assert.IsTrue(preview.SimulateSatisfied(0)); Assert.IsNull(preview.Checkpoint(0));
        Assert.IsFalse(preview.SimulateSatisfied(0)); preview.ResetCheckpoints();
        Assert.AreEqual(1, preview.Seek(0).Current!.Number);
        Assert.AreEqual("Conditional", macro.Editor.Projection.Actions[0].DisplayTime);
    }

    [TestMethod]
    public void VersionedWaitDocumentRejectsLegacyExportAndPreservesUntouchedBytes()
    {
        using var macro = new MacroViewModel("Wait", new FakePlaybackEngine()); macro.AddEvent(new WaitConditionEvent(Condition()));
        var bytes = macro.SnapshotBytes(); var document = RecordingDocument.Read(bytes);
        Assert.AreEqual(3, document.Version); Assert.IsTrue(document.IsExtended);
        Assert.Throws<InvalidOperationException>(() => document.ExportLegacy());
        Assert.Throws<InvalidProtocolBufferException>(() => ProtobufInputEventList.Parser.ParseFrom(bytes), "Legacy protobuf readers reject the invalid-tag envelope.");
        var metadata = RecordingLibraryStore.Describe(Guid.NewGuid(), "Wait", false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, bytes);
        Assert.AreEqual(1, metadata.ConditionalWaitCount); StringAssert.Contains(metadata.Summary, "conditional wait");
        using var restored = new MacroViewModel("Restored", new FakePlaybackEngine()); restored.Restore(new(metadata, bytes));
        CollectionAssert.AreEqual(bytes, restored.SnapshotBytes());
    }

    private sealed class Desktop : IWindowPixelDesktop
    {
        public WindowObservation Found = new([]);
        public PixelObservation Pixel = new(0, "desktop");
        public WindowObservation FindWindows(WindowSelector selector, CancellationToken token) => Found;
        public PixelObservation ReadPixel(PixelCondition condition, ObservedWindow? window) => Pixel;
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void LibraryWaitCountsUseRealPluralsAndOnlyRecordedDelays(int count)
    {
        using var macro = new MacroViewModel("Wait timing", new FakePlaybackEngine());
        for (var i = 0; i < count; i++) macro.AddEvent(new WaitConditionEvent(Condition()) { TimeSinceLastEvent = 125_000 });
        var item = RecordingLibraryStore.Describe(Guid.NewGuid(), "Wait timing", false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, macro.SnapshotBytes());
        Assert.AreEqual(count, item.ConditionalWaitCount);
        Assert.AreEqual((count * 125_000).ToString(), item.DurationMicroseconds);
        if (count == 0) Assert.IsFalse(item.Summary.Contains("conditional wait"));
        else Assert.IsTrue(item.Summary.EndsWith(count == 1 ? "recorded timing + 1 conditional wait" : "recorded timing + 2 conditional waits"));
        Assert.IsFalse(item.Summary.Contains("(s)"));
        Assert.IsFalse(item.Summary.Contains("30 seconds"), "The configured timeout is not recorded timing.");
    }

    [TestMethod]
    public void PreviewCheckpointCutsOffEqualTimePointerInputAndLaterOriginMarkers()
    {
        InputEvent[] inputs = [new WaitConditionEvent(Condition()), new MouseEvent(100, 200, MacroRecorderGUI.Common.MouseActionTypeFlags.Move),
            new WaitConditionEvent(Condition()), new MouseEvent(300, 400, MacroRecorderGUI.Common.MouseActionTypeFlags.Move)];
        var projection = new ActionProjection();
        projection.BeginPointerSegment(new(0, new(1, 2)));
        projection.Append(inputs[0]);
        projection.BeginPointerSegment(new(1, new(20, 30)));
        projection.Append(inputs[1]); projection.Append(inputs[2]);
        projection.BeginPointerSegment(new(3, new(40, 50)));
        projection.Append(inputs[3]);
        var preview = new VisualPreview(inputs, projection);
        Assert.AreEqual(1d, preview.Seek(0).Pointer!.Value.Position!.Value.X);
        preview.SimulateSatisfied(0);
        Assert.AreEqual(100d, preview.Seek(0).Pointer!.Value.Position!.Value.X);
        preview.SimulateSatisfied(0);
        Assert.AreEqual(300d, preview.Seek(0).Pointer!.Value.Position!.Value.X);
        var noOrigin = new ActionProjection(); foreach (var input in inputs) noOrigin.Append(input);
        Assert.IsNull(new VisualPreview(inputs, noOrigin).Seek(0).Pointer);
    }
    [TestMethod]
    public async Task ProviderDistinguishesAbsenceAmbiguityAndUnavailableNegativePixels()
    {
        var desktop = new Desktop(); var observer = new WindowPixelObserver(desktop); var condition = Condition();
        condition.Window.Test = WindowTest.Absent;
        Assert.AreEqual(ObservationState.Match, (await observer.ObserveAsync(condition, default)).State);
        desktop.Found = new([], ObservationState.Unavailable, "denied");
        Assert.AreEqual(ObservationState.Unavailable, (await observer.ObserveAsync(condition, default)).State);
        var window = new ObservedWindow(10, 11, 12, "Class", "Export", "C:\\app.exe", true, false);
        desktop.Found = new([window, window with { Handle = 11 }]);
        Assert.AreEqual(ObservationState.Error, (await observer.ObserveAsync(condition, default)).State);
        condition.Window.Test = WindowTest.Visible; condition.Window.Target.AnyMatch = true;
        Assert.AreEqual(ObservationState.Match, (await observer.ObserveAsync(condition, default)).State);
        condition.Pixel = new() { NotEqual = true, Rgb = 0xffffff };
        desktop.Pixel = new(0, "", "occluded");
        Assert.AreEqual(ObservationState.Unavailable, (await observer.ObserveAsync(condition, default)).State);
        desktop.Pixel = new(0xfffffa, "desktop"); condition.Pixel.Tolerance = 5;
        Assert.AreEqual(ObservationState.NoMatch, (await observer.ObserveAsync(condition, default)).State);
        condition.Pixel.Tolerance = 4;
        Assert.AreEqual(ObservationState.Match, (await observer.ObserveAsync(condition, default)).State);
    }

    private sealed class BlockedObserver : IWaitObserver
    {
        public int Calls;
        public TaskCompletionSource<WaitObservation> Response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<WaitObservation> ObserveAsync(WaitCondition condition, CancellationToken token)
        { Interlocked.Increment(ref Calls); return new(Response.Task); }
    }
    [TestMethod]
    public async Task HungObserverCannotBlockDeadlineOrAccumulateCallsAfterCancellation()
    {
        var observer = new BlockedObserver(); var runner = new WaitRunner(observer);
        var first = await runner.RunAsync(Condition(), TimeSpan.FromMilliseconds(100), default).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse(first.Satisfied); Assert.AreEqual(1, observer.Calls);
        var second = await runner.RunAsync(Condition(), TimeSpan.FromMilliseconds(30), default);
        Assert.IsFalse(second.Satisfied); Assert.AreEqual(1, observer.Calls);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(Condition(), TimeSpan.FromSeconds(1), cancel.Token));
        observer.Response.SetResult(Sample(true));
    }
}
