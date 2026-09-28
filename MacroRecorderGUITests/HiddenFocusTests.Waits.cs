using System.Reflection;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

public sealed partial class HiddenFocusTests
{
    private sealed class HiddenWaitObserver : IWaitObserver
    {
        public int Calls;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<WaitObservation> Response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<WaitObservation> ObserveAsync(WaitCondition condition, CancellationToken token)
        { Interlocked.Increment(ref Calls); Started.TrySetResult(); return new(Response.Task); }
    }

    private static async Task CheckWaitControls(MacroRecorderGUI.MainWindow shell)
    {
        await CheckWaitPickerControls();
        await CheckWaitCaptureSettings();
        await CheckRecordingCaptureCommands();
        await CheckWaitSourceStartup();
        CheckWaitTimingLabels();
        CheckWaitInsertionAfterConditionDraft();
        CheckCancelledWaitInsertionPreservesRawDrafts();
        CheckWaitCheckpointCursor();
        await CheckModalWaitOwnership(shell);
        await CheckLateWaitFeedback();
        using var macro = new MacroViewModel("Wait controls", new FakePlaybackEngine());
        macro.AddEvent(new WaitConditionEvent(ConditionalWaitTests.Condition()));
        macro.AddEvent(new WaitConditionEvent(ConditionalWaitTests.Condition()));
        using var editor = new MacroTabContent { DataContext = macro };
        var window = new Window { Content = editor };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        try
        {
            Assert.IsFalse(IsWindowVisible(hwnd));
            Call(editor, "Attach"); LayoutControl(editor, 1200, 800);
            var actions = Field<ListView>(editor, "ActionsList"); actions.SelectedIndex = 0;
            var fields = Field<WaitConditionEditor>(editor, "_conditionEditor");
            Assert.AreEqual(Visibility.Collapsed, fields.Coordinates.Visibility);
            Assert.AreEqual(Visibility.Visible, fields.WindowRule.Visibility);
            Assert.IsNotNull(fields.Timeout.Style, "Wait fields use the existing approved field style.");
            fields.Timeout.Text = "invalid";
            Assert.IsFalse(editor.TryCommitPendingEdits());
            Assert.AreEqual(30_000_000UL, ((WaitConditionEvent)macro.Events[0]).Condition.TimeoutUs);
            actions.SelectedItem = macro.Editor.Projection.Actions[1];
            Assert.AreSame(macro.Events[0], ((RecordedAction)actions.SelectedItem).First);
            Assert.AreEqual("invalid", fields.Timeout.Text);
            fields.Timeout.Text = "31"; Assert.IsTrue(editor.TryCommitPendingEdits());
            Assert.AreEqual(31_000_000UL, ((WaitConditionEvent)macro.Events[0]).Condition.TimeoutUs);
            Assert.IsTrue(editor.Undo());
            Assert.AreEqual(30_000_000UL, ((WaitConditionEvent)macro.Events[0]).Condition.TimeoutUs);
            fields = Field<WaitConditionEditor>(editor, "_conditionEditor");
            fields.Source.SelectedIndex = 1;
            Assert.AreEqual(3, fields.Trigger.Items.Count, "Pixel conditions do not offer the window-only trigger.");
            Assert.AreEqual(Visibility.Collapsed, fields.WindowRule.Visibility);
            Assert.AreEqual(Visibility.Visible, fields.Coordinates.Visibility);
            fields.X.Text = "-25"; fields.Y.Text = "10"; fields.Rgb.Text = "112233";
            fields.Trigger.SelectedIndex = (int)WaitTrigger.Changes;
            Assert.AreEqual(Visibility.Visible, fields.Rgb.Visibility);
            Assert.AreEqual(Visibility.Visible, fields.NotEqual.Visibility);
            fields.Trigger.SelectedIndex = (int)WaitTrigger.IsTrue;
            Assert.IsTrue(editor.TryCommitPendingEdits());
            Assert.AreEqual(-25, ((WaitConditionEvent)macro.Events[0]).Condition.Pixel.X);
            Assert.AreEqual(0x112233u, ((WaitConditionEvent)macro.Events[0]).Condition.Pixel.Rgb);
            Assert.IsNull(((WaitConditionEvent)macro.Events[0]).Condition.Pixel.Target);
            Call(editor, "RefreshEditor", false);
            LayoutControl(editor, 520, 420);
            Assert.AreEqual(0d, Field<Grid>(editor, "Workspace").Margin.Right);
            Assert.AreEqual(20d, Field<Grid>(editor, "DetailGrid").Margin.Right);
            Assert.AreEqual(0d, Field<Grid>(editor, "DetailGrid").RowDefinitions[1].Height.Value);
            editor.IsPreviewMode = true;
            Assert.AreEqual(Visibility.Visible, Field<Button>(editor, "SimulateCondition").Visibility);
            Assert.AreEqual("ACTION 01 OF 2", Field<TextBlock>(editor, "PreviewStep").Text);
            editor.StepPreview(); Assert.AreEqual("ACTION 02 OF 2", Field<TextBlock>(editor, "PreviewStep").Text);
            editor.TogglePreview(); Assert.AreEqual("ACTION 02 OF 2", Field<TextBlock>(editor, "PreviewStep").Text,
                "Play preview preserves an unsatisfied checkpoint and only restarts a completed zero-time sequence.");
            editor.StepPreview(); Assert.AreEqual(Visibility.Collapsed, Field<Button>(editor, "SimulateCondition").Visibility);
            editor.TogglePreview();
            Assert.AreEqual("ACTION 01 OF 2", Field<TextBlock>(editor, "PreviewStep").Text);
            Assert.AreEqual(Visibility.Visible, Field<Button>(editor, "SimulateCondition").Visibility);
            Assert.IsFalse(editor.IsPreviewPlaying);
            editor.StepPreview(); Assert.AreEqual("ACTION 02 OF 2", Field<TextBlock>(editor, "PreviewStep").Text);
            editor.StepPreview(); Assert.AreEqual(Visibility.Collapsed, Field<Button>(editor, "SimulateCondition").Visibility);
            editor.IsPreviewMode = false;

            // An explicit test uses a fake stuck observer. Command-time commit and
            // detachment cancel the test without launching replacement reads.
            var observer = new HiddenWaitObserver(); var runner = new WaitRunner(observer);
            using var testFields = new WaitConditionEditor(((WaitConditionEvent)macro.Events[0]).Condition, runner);
            Field<WaitConditionEditor>(editor, "_conditionEditor").Dispose();
            typeof(MacroTabContent).GetField("_conditionEditor", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editor, testFields);
            Field<ContentControl>(editor, "WaitConditionHost").Content = testFields;
            Assert.AreEqual(0, observer.Calls);
            var first = testFields.TestAsync(); await observer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsTrue(editor.TryCommitPendingEdits()); await first.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(1, observer.Calls);
            var second = testFields.TestAsync();
            Call(editor, "Detach"); await second.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(1, observer.Calls);
            observer.Response.SetResult(new(ObservationState.Match, "late fake observation"));
            Assert.IsFalse(IsWindowVisible(hwnd));
        }
        finally { window.Close(); }
    }

    private static void CheckWaitInsertionAfterConditionDraft()
    {
        foreach (var replaceDelay in new[] { false, true })
        foreach (var fixedDelayDraft in new[] { false, true })
        foreach (var refreshBeforeConfirmation in new[] { false, true })
        {
            using var macro = new MacroViewModel("Wait insertion", new FakePlaybackEngine());
            var first = new WaitConditionEvent(ConditionalWaitTests.Condition()) { TimeSinceLastEvent = 111 };
            var selected = new WaitConditionEvent(ConditionalWaitTests.Condition()) { TimeSinceLastEvent = 222 };
            macro.AddEvent(first); macro.AddEvent(selected);
            using var editor = new MacroTabContent { DataContext = macro };
            var window = new Window { Content = editor };
            try
            {
                Call(editor, "Attach");
                Field<ListView>(editor, "ActionsList").SelectedIndex = 1;
                Field<WaitConditionEditor>(editor, "_conditionEditor").Timeout.Text = "31";
                if (fixedDelayDraft) Field<TextBox>(editor, "WaitInput").Text = "0.003";
                // This is the exact production preparation/confirmation path, with
                // no dialog shown. Refresh while the dialog would be open as well.
                var apply = (Action<WaitCondition>)Call(editor, "PrepareWaitInsertion", replaceDelay)!;
                Assert.IsNotNull(apply); Assert.AreEqual(31_000_000UL, selected.Condition.TimeoutUs);
                if (refreshBeforeConfirmation) Call(editor, "RefreshEditor", false);
                var added = ConditionalWaitTests.Condition(); added.Window.Target.Title = "Inserted wait";
                apply(added);
                Assert.AreEqual(3, macro.Events.Count); Assert.AreSame(first, macro.Events[0]);
                Assert.AreSame(selected, macro.Events[replaceDelay ? 2 : 1]);
                Assert.AreEqual("Inserted wait", ((WaitConditionEvent)macro.Events[replaceDelay ? 1 : 2]).Condition.Window.Target.Title);
                Assert.AreEqual(replaceDelay ? 0UL : fixedDelayDraft ? 3000UL : 222UL, selected.TimeSinceLastEvent);
                Assert.IsTrue(macro.Editor.Undo()); Assert.AreEqual(2, macro.Events.Count);
                Assert.AreSame(selected, macro.Events[1]); Assert.AreEqual(fixedDelayDraft ? 3000UL : 222UL, selected.TimeSinceLastEvent);
                Assert.AreEqual(31_000_000UL, selected.Condition.TimeoutUs, "Insertion undo does not undo the earlier committed condition draft.");
                Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
            }
            finally { window.Close(); }
        }
    }

    private static void CheckCancelledWaitInsertionPreservesRawDrafts()
    {
        foreach (var replaceDelay in new[] { false, true })
        {
            using var macro = new MacroViewModel("Cancel wait insertion", new FakePlaybackEngine());
            var input = new MouseEvent(20, 30, MacroRecorderGUI.Common.MouseActionTypeFlags.Move) { TimeSinceLastEvent = 500 };
            macro.AddEvent(input); var before = macro.SnapshotBytes();
            using var editor = new MacroTabContent { DataContext = macro };
            var window = new Window { Content = editor };
            try
            {
                Call(editor, "Attach"); Call(editor, "SetRawOpen", true);
                var delay = Field<TextBox>(editor, "RawDelay"); var x = Field<TextBox>(editor, "RawX");
                delay.Text = "unfinished"; x.Text = "901";
                Assert.IsNull(Call(editor, "PrepareWaitInsertion", replaceDelay), "Raw drafts must be resolved before Add or Replace can mutate data.");
                Call(editor, "FinishWaitInsertion", macro.Editor, ContentDialogResult.None);
                Assert.AreSame(input, Field<InputEvent>(editor, "_rawEvent"));
                Assert.AreEqual("unfinished", delay.Text); Assert.AreEqual("901", x.Text);
                CollectionAssert.AreEqual(before, macro.SnapshotBytes()); Assert.IsFalse(macro.Editor.CanUndo);
                Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
            }
            finally { window.Close(); }
        }
    }

    private static void CheckWaitCheckpointCursor()
    {
        foreach (var origins in new[] { false, true })
        {
            using var macro = new MacroViewModel("Wait cursor", new FakePlaybackEngine());
            if (origins) macro.AddCaptureOrigin(new(10, 20));
            macro.AddEvent(new WaitConditionEvent(ConditionalWaitTests.Condition()));
            if (origins) macro.AddCaptureOrigin(new(40, 50));
            macro.AddEvent(new MouseEvent(100, 200, MacroRecorderGUI.Common.MouseActionTypeFlags.Move) { MappedToVirtualDesktop = true });
            macro.AddEvent(new WaitConditionEvent(ConditionalWaitTests.Condition()));
            if (origins) macro.AddCaptureOrigin(new(300, 400));
            macro.AddEvent(new MouseEvent(500, 600, MacroRecorderGUI.Common.MouseActionTypeFlags.Move) { MappedToVirtualDesktop = true });
            using var editor = new MacroTabContent { DataContext = macro };
            var window = new Window { Content = editor };
            try
            {
                Call(editor, "Attach"); LayoutControl(editor, 1200, 800); editor.IsPreviewMode = true;
                var canvas = Field<Canvas>(editor, "PathCanvas");
                canvas.Measure(new Windows.Foundation.Size(600, 400)); canvas.Arrange(new Windows.Foundation.Rect(0, 0, 600, 400));
                Call(editor, "DrawPath"); Check(origins ? new(10, 20, CoordinateSpace.AbsoluteDesktop) : null);
                editor.StepPreview(); Check(new(100, 200, CoordinateSpace.AbsoluteDesktop));
                Call(editor, "SimulateCondition_Click", editor, new RoutedEventArgs()); Check(new(500, 600, CoordinateSpace.AbsoluteDesktop));
                editor.IsPreviewMode = false; editor.IsPreviewMode = true;
                Check(origins ? new(10, 20, CoordinateSpace.AbsoluteDesktop) : null);
                Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
            }
            finally { window.Close(); }

            void Check(PathPosition? expected)
            {
                var cursor = Field<Microsoft.UI.Xaml.Shapes.Polygon>(editor, "_cursor");
                Assert.IsNotNull(cursor);
                Assert.AreEqual(expected is null ? Visibility.Collapsed : Visibility.Visible, cursor.Visibility);
                if (expected is { } position)
                {
                    var point = Field<PathViewport>(editor, "_viewport").Map(position);
                    Assert.AreEqual(point.X, Canvas.GetLeft(cursor), 0.001);
                    Assert.AreEqual(point.Y, Canvas.GetTop(cursor), 0.001);
                }
            }
        }
    }

    private static async Task CheckModalWaitOwnership(MacroRecorderGUI.MainWindow shell)
    {
        var engine = new FakePlaybackEngine();
        using var macro = new MacroViewModel("Modal wait", engine);
        macro.AddEvent(new WaitConditionEvent(ConditionalWaitTests.Condition()));
        using var editor = new MacroTabContent { DataContext = macro };
        var host = Field<ContentControl>(shell, "EditorHost"); var previous = host.Content;
        var observer = new HiddenWaitObserver(); var runner = new WaitRunner(observer);
        using var fields = new WaitConditionEditor(ConditionalWaitTests.Condition(), runner);
        try
        {
            host.Content = editor; Call(editor, "Attach"); Assert.IsTrue(editor.TryCommitPendingEdits());
            Call(editor, "BeginWaitDialog", fields);
            var test = fields.TestAsync(); await observer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var enteredRunPreparation = false;
            Assert.IsFalse(Field<bool>(shell, "_busy"));
            // OperationAsync is the common Play/Ctrl+E gate. A fake delegate
            // proves preparation is never entered; no controller can be shown.
            await (Task)Call(shell, "OperationAsync", (Func<Task>)(() => { enteredRunPreparation = true; return Task.CompletedTask; }))!;
            Assert.IsFalse(enteredRunPreparation); Assert.AreEqual(0, engine.Starts);
            await test.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsFalse(editor.TryCommitPendingEdits(), "The modal remains a run gate until closed.");
            var second = fields.TestAsync(); Call(editor, "EndWaitDialog", fields);
            await second.WaitAsync(TimeSpan.FromSeconds(2)); Assert.IsTrue(editor.TryCommitPendingEdits());
            Call(editor, "BeginWaitDialog", fields);
            var third = fields.TestAsync(); Call(editor, "Detach");
            await third.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(1, observer.Calls, "Cancelled modal tests share the same resource bound.");
            Assert.IsNull(Field<WaitConditionEditor?>(editor, "_modalConditionEditor"));
            Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(shell)));
        }
        finally { observer.Response.TrySetResult(new(ObservationState.Match, "late")); host.Content = previous; }
    }

    private sealed class QueuedWaitContext : SynchronizationContext
    {
        public readonly TaskCompletionSource Posted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _pending = new();
        public override void Post(SendOrPostCallback callback, object? state) { _pending.Enqueue((callback, state)); Posted.TrySetResult(); }
        public void Drain() { while (_pending.TryDequeue(out var work)) work.Callback(work.State); }
    }
    private static async Task CheckLateWaitFeedback()
    {
        foreach (var edit in new[] { false, true })
        {
            var condition = ConditionalWaitTests.Condition(); condition.StableForUs = 0;
            var observer = new HiddenWaitObserver();
            using var fields = new WaitConditionEditor(condition, new WaitRunner(observer));
            var queue = new QueuedWaitContext(); var previous = SynchronizationContext.Current;
            Task test;
            try { SynchronizationContext.SetSynchronizationContext(queue); test = fields.TestAsync(); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            Assert.IsFalse(test.IsCompleted);
            // Completion is impossible until TestAsync has suspended and captured
            // the queued context, regardless of thread-pool scheduling speed.
            observer.Response.SetResult(new(ObservationState.Match, "queued fake success"));
            // Deliberately hold UI dispatch until the fake success and its progress
            // are queued, then invalidate both before either may publish.
            Assert.IsTrue(queue.Posted.Task.Wait(TimeSpan.FromSeconds(2)));
            if (edit) fields.Title.Text = "Edited target"; else fields.CancelTest();
            var expected = edit ? "Unapplied condition changes" : "Condition test cancelled.";
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fields.DispatcherQueue.TryEnqueue(() => drained.TrySetResult());
            await drained.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(expected, fields.Feedback.Text, "Queued progress cannot overwrite an edit or cancellation.");
            queue.Drain(); await test.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(expected, fields.Feedback.Text, "Queued terminal success cannot overwrite an edit or cancellation.");
        }
    }

    private static void CheckWaitTimingLabels()
    {
        for (var count = 0; count <= 2; count++)
        {
            using var macro = new MacroViewModel("Wait timing", new FakePlaybackEngine());
            for (var i = 0; i < count; i++) macro.AddEvent(new WaitConditionEvent(ConditionalWaitTests.Condition()));
            using var editor = new MacroTabContent { DataContext = macro };
            var window = new Window { Content = editor };
            try
            {
                Call(editor, "Attach"); editor.IsPreviewMode = true;
                var expected = count switch { 0 => "Recorded timing · 0 actions", 1 => "Recorded timing + 1 conditional wait", _ => "Recorded timing + 2 conditional waits" };
                Assert.AreEqual(expected, Field<TextBlock>(editor, "PreviewTiming").Text);
                Assert.IsFalse(editor.Summary.Contains("(s)"));
                if (count > 0) StringAssert.Contains(editor.Summary, count == 1 ? "1 conditional wait" : "2 conditional waits");
                Assert.IsFalse(editor.Summary.Contains("30s"), "A timeout is not recorded timing.");
                Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
            }
            finally { window.Close(); }
        }
    }
}
