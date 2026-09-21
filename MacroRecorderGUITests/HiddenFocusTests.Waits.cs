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

    private static async Task CheckWaitControls()
    {
        CheckWaitTimingLabels();
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
            Assert.AreEqual(Visibility.Collapsed, fields.Rgb.Visibility);
            Assert.AreEqual(Visibility.Collapsed, fields.NotEqual.Visibility);
            fields.Trigger.SelectedIndex = (int)WaitTrigger.IsTrue;
            Assert.IsTrue(editor.TryCommitPendingEdits());
            Assert.AreEqual(-25, ((WaitConditionEvent)macro.Events[0]).Condition.Pixel.X);
            Assert.AreEqual(0x112233u, ((WaitConditionEvent)macro.Events[0]).Condition.Pixel.Rgb);
            Assert.IsNull(((WaitConditionEvent)macro.Events[0]).Condition.Pixel.Target);
            Call(editor, "RefreshEditor", false);
            LayoutControl(editor, 520, 420);
            Assert.AreEqual(20d, Field<Grid>(editor, "Workspace").Margin.Right);
            Assert.AreEqual(20d, Field<Grid>(editor, "DetailGrid").Margin.Right);
            Assert.AreEqual(0d, Field<Grid>(editor, "DetailGrid").RowDefinitions[1].Height.Value);
            editor.IsPreviewMode = true;
            Assert.AreEqual(Visibility.Visible, Field<Button>(editor, "SimulateCondition").Visibility);
            Assert.AreEqual("ACTION 01 OF 2", Field<TextBlock>(editor, "PreviewStep").Text);
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
