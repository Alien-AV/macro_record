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
    private static MacroViewModel ReviewMacro()
    {
        var macro = new MacroViewModel("Editor review", new FakePlaybackEngine());
        macro.AddCaptureOrigin(new(-100, 200));
        for (var i = 0; i < 3; i++)
            macro.AddEvent(new MouseEvent(i * 10, i * 20, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 100 });
        macro.AddEvent(new WaitConditionEvent(ConditionalWaitTests.Condition()));
        return macro;
    }

    private static void CheckEditorDeleteScope()
    {
        foreach (var (width, resize) in new[] { (420d, false), (1200d, false), (1200d, true) })
        foreach (var command in new[] { "key", "active pane", "sequence menu" })
        {
            using var macro = ReviewMacro();
            using var editor = new MacroTabContent { DataContext = macro };
            var window = new Window { Content = editor };
            try
            {
                Call(editor, "Attach"); LayoutControl(editor, width, 600);
                var actions = Field<ListView>(editor, "ActionsList"); actions.SelectedIndex = 0;
                Call(editor, "SetRawOpen", true); LayoutControl(editor, width, 600);
                var raw = Field<ListView>(editor, "RawList"); raw.SelectedItem = raw.Items[1];
                var primary = macro.Events[1]; var original = macro.Events.ToArray(); var before = macro.SnapshotBytes();
                SwitchPane();
                if (resize) LayoutControl(editor, 420, 600);
                Assert.AreEqual(Visibility.Visible, Field<Border>(editor, "ListPanel").Visibility);
                Assert.AreEqual("1 action selected", Field<TextBlock>(editor, "SelectionScope").Text);
                Assert.IsTrue(macro.Editor.RawSelection, "Navigation retains the raw subset independently of command scope.");
                SwitchPane();
                Assert.AreSame(primary, Field<InputEvent>(editor, "_rawEvent"));
                CollectionAssert.AreEqual(new[] { primary }, raw.SelectedItems.Cast<RawEventRow>().Select(row => row.Input).ToArray());
                SwitchPane();
                if (command == "key") Assert.IsTrue((bool)Call(editor, "HandleDeleteKey", actions, VirtualKey.Delete, null)!);
                else Call(editor, command == "sequence menu" ? "DeleteActions_Click" : "Delete_Click", editor, new RoutedEventArgs());
                CollectionAssert.AreEqual(new[] { original[3] }, macro.Events.ToArray(), "Sequence Delete removes the whole action, not the hidden raw subset.");
                Assert.IsTrue(editor.Undo()); CollectionAssert.AreEqual(before, macro.SnapshotBytes());
                CollectionAssert.AreEqual(original, macro.Events.ToArray());

                Call(editor, "ShowPane", true);
                raw.SelectedItem = raw.Items[1];
                Assert.IsTrue((bool)Call(editor, "HandleDeleteKey", raw, VirtualKey.Delete, null)!);
                CollectionAssert.AreEqual(new[] { original[0], original[2], original[3] }, macro.Events.ToArray(), "Exact input Delete removes only its selected event.");
                Assert.IsTrue(editor.Undo()); CollectionAssert.AreEqual(before, macro.SnapshotBytes());

                // The sequence context menu always belongs to actions, including
                // when both panes are visible and Details most recently had focus.
                if (width == 1200 && !resize)
                {
                    raw.SelectedItem = raw.Items[1]; Call(editor, "ShowPane", true);
                    Call(editor, "DeleteActions_Click", editor, new RoutedEventArgs());
                    CollectionAssert.AreEqual(new[] { original[3] }, macro.Events.ToArray());
                    Assert.IsTrue(editor.Undo()); CollectionAssert.AreEqual(before, macro.SnapshotBytes());
                }
                Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
                void SwitchPane() => Assert.IsTrue((bool)Call(editor, "HandlePaneKey", VirtualKey.F6, false, false, false)!);
            }
            finally { window.Close(); }
        }
    }

    private static void CheckDescendingRawDraftOwnership()
    {
        foreach (var draft in new[] { "unfinished", "999999" })
        {
            using var macro = ReviewMacro();
            using var editor = new MacroTabContent { DataContext = macro };
            var window = new Window { Content = editor };
            try
            {
                Call(editor, "Attach"); LayoutControl(editor, 1200, 600); Call(editor, "SetRawOpen", true);
                var raw = Field<ListView>(editor, "RawList");
                raw.SelectedItem = raw.Items[2]; raw.SelectedItems.Add(raw.Items[0]);
                var primary = macro.Events[2]; var subset = new[] { macro.Events[0], primary };
                var delay = Field<TextBox>(editor, "RawDelay"); delay.Text = draft; delay.Select(1, 3);
                var before = macro.SnapshotBytes();
                CheckDraft();
                Call(editor, "SetRawOpen", false); Call(editor, "ShowPane", false); LayoutControl(editor, 420, 600);
                Assert.IsFalse(editor.TryCommitPendingEdits());
                Call(editor, "Delete_Click", editor, new RoutedEventArgs()); CollectionAssert.AreEqual(before, macro.SnapshotBytes());
                Call(editor, "ShowPane", true); Call(editor, "SetRawOpen", true); CheckDraft();
                raw.SelectedItem = raw.Items[1]; CheckDraft();
                // A no-op timing edit rebuilds the projection without modifying
                // bytes, forcing raw rows to be recreated around the same inputs.
                macro.Editor.SetWait(macro.Editor.Projection.Actions[0], macro.Events[0].TimeSinceLastEvent);
                Call(editor, "RefreshEditor", false); CheckDraft();
                CollectionAssert.AreEqual(before, macro.SnapshotBytes());
                delay.Text = "999999"; Call(editor, "RawApply_Click", editor, new RoutedEventArgs());
                Assert.AreEqual(999999UL, primary.TimeSinceLastEvent);
                Assert.AreEqual(100UL, macro.Events[0].TimeSinceLastEvent, "Apply must target the original later primary event.");
                Assert.IsTrue(editor.Undo()); CollectionAssert.AreEqual(before, macro.SnapshotBytes());
                Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
                void CheckDraft()
                {
                    Assert.AreSame(primary, Field<InputEvent>(editor, "_rawEvent"));
                    Assert.AreSame(primary, ((RawEventRow)raw.SelectedItem).Input);
                    CollectionAssert.AreEquivalent(subset, raw.SelectedItems.Cast<RawEventRow>().Select(row => row.Input).ToArray());
                    Assert.AreEqual(draft, delay.Text); Assert.AreEqual(1, delay.SelectionStart); Assert.AreEqual(3, delay.SelectionLength);
                }
            }
            finally { window.Close(); }
        }
    }

    private static void CheckAdvancedAddFromEmpty()
    {
        foreach (var add in new[] { "AddMouse_Click", "AddKeyboard_Click" })
        foreach (var rawAlreadyOpen in new[] { false, true })
        {
            using var macro = new MacroViewModel("Empty Add", new FakePlaybackEngine());
            var before = macro.SnapshotBytes();
            using var editor = new MacroTabContent { DataContext = macro };
            var window = new Window { Content = editor };
            try
            {
                Call(editor, "Attach"); LayoutControl(editor, 420, 600);
                if (rawAlreadyOpen) Call(editor, "SetRawOpen", true);
                Call(editor, add, editor, new RoutedEventArgs()); LayoutControl(editor, 420, 600);
                Assert.AreEqual(1, macro.Events.Count);
                var raw = Field<ListView>(editor, "RawList"); var delay = Field<TextBox>(editor, "RawDelay");
                Assert.AreSame(macro.Events[0], ((RawEventRow)raw.SelectedItem).Input);
                Assert.AreSame(macro.Events[0], Field<InputEvent>(editor, "_rawEvent"));
                CollectionAssert.AreEqual(new[] { macro.Events[0] }, macro.SelectedEvents.ToArray());
                Assert.IsTrue(delay.IsEnabled); Assert.IsTrue(Field<Button>(editor, "RawApply").IsEnabled);
                Assert.AreEqual(Visibility.Visible, Field<Border>(editor, "InspectorPanel").Visibility);
                Assert.AreEqual(Visibility.Visible, Field<Grid>(editor, "RawContent").Visibility);
                delay.Text = "123"; Call(editor, "RawApply_Click", editor, new RoutedEventArgs());
                Assert.AreEqual(123UL, macro.Events[0].TimeSinceLastEvent);
                Assert.IsTrue(editor.Undo()); Assert.AreEqual(0UL, macro.Events[0].TimeSinceLastEvent);
                Assert.IsTrue(editor.Undo()); CollectionAssert.AreEqual(before, macro.SnapshotBytes());
                Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
            }
            finally { window.Close(); }
        }
    }
}
