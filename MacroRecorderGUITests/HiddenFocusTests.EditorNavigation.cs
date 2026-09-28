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
    private static void CheckEditorNavigationAndAuthoring()
    {
        CheckEditorDeleteScope();
        CheckDescendingRawDraftOwnership();
        CheckAdvancedAddFromEmpty();
        using var macro = new MacroViewModel("Navigation", new FakePlaybackEngine());
        macro.AddEvent(new MouseEvent(10, 20, MouseActionTypeFlags.Move));
        macro.AddEvent(new MouseEvent(30, 40, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 100 });
        macro.AddEvent(new WaitConditionEvent(ConditionalWaitTests.Condition()));
        using var editor = new MacroTabContent { DataContext = macro };
        var window = new Window { Content = editor };
        try
        {
            Call(editor, "Attach"); LayoutControl(editor, 420, 520);
            var actions = Field<ListView>(editor, "ActionsList"); actions.SelectedIndex = 0;
            Call(editor, "ShowPane", true); Call(editor, "SetRawOpen", true);
            LayoutControl(editor, 420, 520);
            var raw = Field<ListView>(editor, "RawList");
            raw.SelectedItems.Add(raw.Items[1]);
            var selectedRaw = raw.SelectedItems.Cast<RawEventRow>().Select(row => row.Input).ToArray();
            var delay = Field<TextBox>(editor, "RawDelay"); var pause = Field<TextBox>(editor, "WaitInput");
            delay.Text = "invalid"; pause.Text = "0.000123"; delay.Select(1, 3);
            var before = macro.SnapshotBytes();
            Call(editor, "RememberFocus", delay);
            Call(editor, "SetRawOpen", false);
            Assert.AreSame(delay, Field<Control>(editor, "_rawFocus"));
            Assert.AreEqual(Visibility.Visible, Field<ScrollViewer>(editor, "InspectorScroller").Visibility);
            Assert.IsFalse(editor.TryCommitPendingEdits());
            Assert.IsFalse(editor.Undo());
            Call(editor, "AddMouse_Click", editor, new RoutedEventArgs());
            Call(editor, "Delete_Click", editor, new RoutedEventArgs());
            Assert.IsNull(Call(editor, "PrepareActionInsertion"));
            Assert.IsFalse(editor.IsPreviewMode); editor.IsPreviewMode = true; Assert.IsFalse(editor.IsPreviewMode);
            foreach (var key in new[] { VirtualKey.A, VirtualKey.Delete, VirtualKey.Escape, VirtualKey.Z })
                Assert.IsFalse((bool)Call(editor, "HandlePaneKey", key, false, false, false)!);
            foreach (var modifiers in new[] { (true, false, false), (false, true, false), (false, false, true) })
                Assert.IsFalse((bool)Call(editor, "HandlePaneKey", VirtualKey.F6, modifiers.Item1, modifiers.Item2, modifiers.Item3)!);
            Assert.IsTrue((bool)Call(editor, "HandlePaneKey", VirtualKey.F6, false, false, false)!);
            Assert.AreEqual(Visibility.Visible, Field<Border>(editor, "ListPanel").Visibility);
            Assert.AreEqual(Visibility.Collapsed, Field<Border>(editor, "InspectorPanel").Visibility);
            Assert.IsTrue((bool)Call(editor, "HandlePaneKey", VirtualKey.F6, false, false, false)!);
            Assert.AreEqual(Visibility.Visible, Field<Border>(editor, "InspectorPanel").Visibility);
            Call(editor, "SetRawOpen", true);
            CollectionAssert.AreEqual(selectedRaw, raw.SelectedItems.Cast<RawEventRow>().Select(row => row.Input).ToArray());
            Assert.AreEqual("invalid", delay.Text); Assert.AreEqual(1, delay.SelectionStart); Assert.AreEqual(3, delay.SelectionLength);
            CollectionAssert.AreEqual(before, macro.SnapshotBytes());
            raw.SelectedItem = raw.Items[1];
            CollectionAssert.AreEqual(selectedRaw, raw.SelectedItems.Cast<RawEventRow>().Select(row => row.Input).ToArray(), "Row navigation cannot discard an unfinished draft.");
            delay.Text = "200";
            Assert.IsFalse(editor.TryCommitPendingEdits(), "Valid explicit raw drafts still require Apply or Discard.");
            Call(editor, "RawApply_Click", editor, new RoutedEventArgs());
            Assert.AreEqual(200UL, macro.Events[0].TimeSinceLastEvent);
            Assert.IsTrue(editor.TryCommitPendingEdits());
            delay.Text = "bad"; Call(editor, "RawDiscard_Click", editor, new RoutedEventArgs());
            Assert.IsTrue(editor.TryCommitPendingEdits());

            actions.SelectedItem = macro.Editor.Projection.Actions.Last();
            var condition = Field<WaitConditionEditor>(editor, "_conditionEditor");
            condition.Timeout.Text = "unfinished";
            before = macro.SnapshotBytes();
            Call(editor, "SetRawOpen", true); Call(editor, "ShowPane", false); Call(editor, "ShowPane", true); Call(editor, "SetRawOpen", false);
            Assert.AreSame(condition, Field<WaitConditionEditor>(editor, "_conditionEditor"));
            Assert.AreEqual("unfinished", condition.Timeout.Text); CollectionAssert.AreEqual(before, macro.SnapshotBytes());
            Assert.IsFalse(editor.TryCommitPendingEdits());
            condition.Timeout.Text = "30"; Assert.IsTrue(editor.TryCommitPendingEdits());
            CheckAuthoringDialogs(editor, macro);
            Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
        }
        finally { window.Close(); }
    }

    private static void CheckAuthoringDialogs(MacroTabContent editor, MacroViewModel macro)
    {
        var style = (Style)editor.Resources.MergedDictionaries[0]["DesignedEditorField"];
        var actions = Field<ListView>(editor, "ActionsList");
        foreach (var kind in Enum.GetValues<AuthoringKind>())
        {
            actions.SelectedItem = macro.Editor.Projection.Actions.Last();
            var anchor = ((RecordedAction)actions.SelectedItem).First;
            var insert = (Action<ActionAuthoringFields>)Call(editor, "PrepareActionInsertion")!;
            var fields = new ActionAuthoringFields(kind, style);
            fields.Hold.Text = "0.000456";
            fields.ControlKey.IsChecked = true;
            fields.X.Text = "-123"; fields.Y.Text = "456";
            var before = macro.SnapshotBytes();
            Call(editor, "BeginAuthoringDialog");
            Assert.IsFalse(editor.TryCommitPendingEdits());
            Assert.IsFalse((bool)Call(editor, "HandlePaneKey", VirtualKey.F6, false, false, false)!);
            Call(editor, "SetRawOpen", false); CollectionAssert.AreEqual(before, macro.SnapshotBytes());
            if (kind == AuthoringKind.PointerMovement) fields.X.Text = "invalid";
            else if (kind == AuthoringKind.Delay) fields.Delay.Text = "invalid";
            else fields.Hold.Text = "invalid";
            try { insert(fields); Assert.Fail("An invalid numeric field must be rejected."); }
            catch (Exception error) when (error is ArgumentException or FormatException) { }
            CollectionAssert.AreEqual(before, macro.SnapshotBytes());
            fields.X.Text = "-123"; fields.Hold.Text = "0.000456"; fields.Delay.Text = "0.000123";
            // Change the projection while the form remains open; its input identity stays stable.
            macro.Editor.SetWait(macro.Editor.Projection.Actions.Last(), 99); Call(editor, "RefreshEditor", false);
            before = macro.SnapshotBytes();
            insert(fields);
            Call(editor, "EndAuthoringDialog"); Call(editor, "RefreshEditor", false);
            var anchorIndex = macro.Events.IndexOf(anchor);
            Assert.AreEqual(0UL, macro.Events[anchorIndex + 1].TimeSinceLastEvent);
            Assert.IsTrue(macro.Editor.Undo()); Call(editor, "RefreshEditor", false);
            CollectionAssert.AreEqual(before, macro.SnapshotBytes(), "One Undo removes the complete authored action.");
        }
        var stale = (Action<ActionAuthoringFields>)Call(editor, "PrepareActionInsertion")!;
        var fieldsAfterClose = new ActionAuthoringFields(AuthoringKind.Click, style);
        Call(editor, "Detach");
        Assert.ThrowsExactly<ArgumentException>(() => stale(fieldsAfterClose));
    }
}
