using MacroRecorderGUI.Common;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace MacroRecorderGUITests;

public sealed partial class HiddenFocusTests
{
    private static void CheckEditorActions()
    {
        CheckLeadingDelaySelectionScope();
        using var macro = new MacroViewModel("Editor actions", new FakePlaybackEngine());
        macro.AddEvent(new MouseEvent(10, 20, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 500000 });
        macro.AddEvent(new MouseEvent(30, 40, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 200 });
        using var editor = new MacroTabContent { DataContext = macro };
        var window = new Window { Content = editor };
        try
        {
            void Blur(TextBox field)
            {
                editor.IsTabStop = true;
                editor.Focus(FocusState.Programmatic);
                Call(editor, "ActionField_LostFocus", field, new RoutedEventArgs());
            }
            Call(editor, "Attach"); LayoutControl(editor, 1200, 800);
            var actions = Field<ListView>(editor, "ActionsList"); actions.SelectedIndex = 0;
            Assert.AreEqual(Visibility.Collapsed, Field<StackPanel>(editor, "PaneNavigation").Visibility);
            Assert.AreEqual(Visibility.Collapsed, Field<StackPanel>(editor, "DelayFields").Visibility);
            Assert.IsTrue((bool)Call(editor, "HandlePaneKey", VirtualKey.F6, false, false, false)!);
            Assert.AreEqual(Visibility.Visible, Field<Border>(editor, "ListPanel").Visibility);
            Assert.AreEqual(Visibility.Visible, Field<Border>(editor, "InspectorPanel").Visibility);
            var add = Field<Button>(editor, "AddAction");
            Assert.AreEqual("Add event", Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(add));
            CollectionAssert.AreEqual(new[] { "Click…", "Shortcut…", "Pointer movement…", "Delay…", "Wait until…" },
                ((MenuFlyout)add.Flyout).Items.OfType<MenuFlyoutItem>().Select(item => item.Text).ToArray());
            LayoutControl(editor, 420, 520);
            Assert.AreEqual(Visibility.Visible, Field<StackPanel>(editor, "PaneNavigation").Visibility);
            Call(editor, "ShowPane", false);
            Assert.AreEqual(Visibility.Collapsed, Field<Border>(editor, "InspectorPanel").Visibility);
            Call(editor, "ShowPane", true);
            Assert.AreEqual(Visibility.Collapsed, Field<Border>(editor, "ListPanel").Visibility);

            Call(editor, "SelectLeadingDelay", macro.Editor.Projection.Actions[0]);
            Assert.AreEqual("Delay", Field<TextBlock>(editor, "SelectedTitle").Text);
            var delay = Field<TextBox>(editor, "WaitInput");
            delay.Text = "";
            Blur(delay);
            Assert.AreEqual(0UL, macro.Events[0].TimeSinceLastEvent);
            Assert.AreEqual("0", delay.Text, "The first blur normalizes an empty delay.");
            delay.Text = "unfinished"; delay.Select(2, 4);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                Blur(delay);
                Call(editor, "RefreshEditor", false);
                Assert.AreEqual("unfinished", delay.Text);
                Assert.AreEqual(2, delay.SelectionStart); Assert.AreEqual(4, delay.SelectionLength);
            }
            delay.Text = "0"; Assert.IsTrue(editor.TryCommitPendingEdits());
            var duration = Field<TextBox>(editor, "DurationInput");
            duration.Text = "";
            Blur(duration);
            Assert.AreEqual(0UL, macro.Events[1].TimeSinceLastEvent);
            Assert.AreEqual("0", duration.Text);
            macro.AddEvent(new MouseEvent(50, 60, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 500000 });
            Call(editor, "RefreshEditor", false);
            Call(editor, "SelectLeadingDelay", macro.Editor.Projection.Actions.Last());
            var gapOwner = macro.Events[2];
            delay.Text = "";
            var shown = new List<string>();
            delay.TextChanging += (_, _) => shown.Add(delay.Text);
            Blur(delay);
            Assert.AreEqual(1, macro.Editor.Projection.Actions.Count, "Clearing this gap merges the movement groups.");
            Assert.AreSame(gapOwner, Field<InputEvent>(editor, "_leadingDelayAnchor"));
            Assert.AreEqual("Delay", Field<TextBlock>(editor, "SelectedTitle").Text);
            CollectionAssert.AreEqual(new[] { "0" }, shown, "One blur must not restore another action's timing before normalizing zero.");
            Assert.AreEqual(Visibility.Collapsed, macro.Editor.Projection.Actions.Single().LeadingDelayVisibility);
            delay.Text = "invalid"; delay.Select(1, 3); shown.Clear();
            Blur(delay);
            Call(editor, "RefreshEditor", false);
            Assert.IsEmpty(shown, "Invalid drafts must not be rewritten during selection rebuilds.");
            Assert.AreEqual(1, delay.SelectionStart); Assert.AreEqual(3, delay.SelectionLength);
            delay.Text = "0.000123"; Blur(delay);
            Assert.AreEqual(123UL, gapOwner.TimeSinceLastEvent);
            Assert.AreEqual(0UL, macro.Events[0].TimeSinceLastEvent);
            Call(editor, "ActionBody_Tapped", editor, null);
            var x = Field<TextBox>(editor, "DestinationX");
            var before = macro.SnapshotBytes(); x.Text = "";
            Blur(x);
            Assert.AreEqual("", x.Text, "An empty coordinate stays invalid; it is not a zero default.");
            CollectionAssert.AreEqual(before, macro.SnapshotBytes());
            x.Text = "50"; Assert.IsTrue(editor.TryCommitPendingEdits());
            var inserted = macro.Editor.InsertDelay(macro.Editor.Projection.Actions.Last(), 123);
            Call(editor, "RefreshEditor", false);
            actions.SelectedItem = macro.Editor.Projection.Actions.Last();
            Assert.AreEqual("Delay", Field<TextBlock>(editor, "SelectedTitle").Text);
            delay.Text = ""; Blur(delay);
            Assert.AreEqual(0UL, inserted.DurationMicroseconds);
            Assert.AreEqual("0", delay.Text);
            Assert.AreSame(inserted, ((RecordedAction)actions.SelectedItem).First);
            Call(editor, "SetRawOpen", true);
            var rawDuration = Field<TextBox>(editor, "RawFixedDuration");
            Assert.AreEqual(Visibility.Visible, rawDuration.Visibility);
            rawDuration.Text = "42";
            Call(editor, "RawApply_Click", editor, new RoutedEventArgs());
            Assert.AreEqual(42UL, inserted.DurationMicroseconds);
            Assert.IsTrue(editor.Undo()); Assert.AreEqual(0UL, inserted.DurationMicroseconds);
            editor.IsPreviewMode = true;
            Assert.IsTrue(editor.IsPreviewMode);
            Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
        }
        finally { window.Close(); }
    }

    private static void CheckLeadingDelaySelectionScope()
    {
        foreach (var width in new[] { 420d, 1200d })
        {
            using var macro = new MacroViewModel("Delay selection scope", new FakePlaybackEngine());
            macro.AddEvent(new MouseEvent(10, 20, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 500000 });
            macro.AddEvent(new MouseEvent(30, 40, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 600000 });
            var original = macro.SnapshotBytes();
            using var editor = new MacroTabContent { DataContext = macro };
            var window = new Window { Content = editor };
            try
            {
                Call(editor, "Attach"); LayoutControl(editor, width, 650);
                var actions = Field<ListView>(editor, "ActionsList");
                Assert.AreEqual(4, macro.Editor.Projection.StepCount);
                StringAssert.StartsWith(editor.Summary, "4 steps");
                CollectionAssert.AreEqual(new[] { "02", "04" }, macro.Editor.Projection.Actions.Select(a => a.DisplayNumber).ToArray());
                CollectionAssert.AreEqual(new[] { "01", "03" }, macro.Editor.Projection.Actions.Select(a => a.LeadingDelayNumber).ToArray());
                editor.IsPreviewMode = true;
                Assert.AreEqual("ACTION 01 OF 4", Field<TextBlock>(editor, "PreviewStep").Text);
                editor.StepPreview();
                Assert.AreEqual("ACTION 03 OF 4", Field<TextBlock>(editor, "PreviewStep").Text,
                    "Zero-duration input completes at the end of the first delay; preview then shows the next delay.");
                editor.IsPreviewMode = false;

                Call(editor, "SelectLeadingDelay", macro.Editor.Projection.Actions[0]);
                Call(editor, "ActionBody_Tapped", editor, null);
                Assert.IsNull(Field<InputEvent?>(editor, "_leadingDelayAnchor"));
                Assert.AreEqual("1 action selected", Field<TextBlock>(editor, "SelectionScope").Text);
                Assert.AreEqual("Delete selected actions", Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(Field<Button>(editor, "DeleteButton")));

                Call(editor, "SelectLeadingDelay", macro.Editor.Projection.Actions[0]);
                actions.SelectedItems.Add(macro.Editor.Projection.Actions[1]);
                Assert.IsNull(Field<InputEvent?>(editor, "_leadingDelayAnchor"), "Extending action selection leaves delay-only deletion scope.");
                Assert.AreEqual("2 actions selected", Field<TextBlock>(editor, "SelectionScope").Text);
                Call(editor, "DeleteActions_Click", editor, new RoutedEventArgs());
                Assert.AreEqual(0, macro.Events.Count, "Action multi-delete removes both inputs, not just the first gap.");
                Assert.IsTrue(editor.Undo()); CollectionAssert.AreEqual(original, macro.SnapshotBytes());

                Call(editor, "SelectLeadingDelay", macro.Editor.Projection.Actions[0]);
                Call(editor, "SetRawOpen", true);
                var raw = Field<ListView>(editor, "RawList");
                Assert.AreSame(macro.Events[0], ((RawEventRow)raw.SelectedItem).Input);
                Call(editor, "SelectLeadingDelay", macro.Editor.Projection.Actions[0]);
                Assert.IsFalse(Field<bool>(editor, "_rawOpen"), "Selecting a Delay opens its details even if Exact input was previously open.");
                Call(editor, "Delete_Click", Field<Button>(editor, "DeleteButton"), new RoutedEventArgs());
                Assert.AreEqual(2, macro.Events.Count, "Delete shown as delay-only must not delete its raw input.");
                Assert.AreEqual(0UL, macro.Events[0].TimeSinceLastEvent);
                Assert.IsTrue(editor.Undo()); CollectionAssert.AreEqual(original, macro.SnapshotBytes());
                Call(editor, "SetRawOpen", true);
                Call(editor, "SetRawOpen", false);
                actions.SelectedItem = macro.Editor.Projection.Actions[1];
                Assert.IsNull(Field<InputEvent?>(editor, "_leadingDelayAnchor"));
                Call(editor, "DrawPath");
                StringAssert.StartsWith(Field<TextBlock>(editor, "PathSelectionNote").Text, "Action 04",
                    "Path labels use the same visible step number as the sequence list.");
                Call(editor, "SetRawOpen", true);
                Assert.AreSame(macro.Events[1], ((RawEventRow)raw.SelectedItem).Input);
                Call(editor, "HandleDeleteKey", raw, VirtualKey.Delete, null);
                Assert.AreEqual(1, macro.Events.Count);
                Assert.AreEqual(500000UL, macro.Events[0].TimeSinceLastEvent, "Raw Delete must not edit the formerly selected delay.");
                Assert.IsTrue(editor.Undo()); CollectionAssert.AreEqual(original, macro.SnapshotBytes());
                Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
            }
            finally { window.Close(); }
        }
    }
}
