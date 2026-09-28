using Google.Protobuf;
using MacroRecorderGUI;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MacroRecorderGUITests;

public sealed partial class HiddenFocusTests
{
    private static void CheckDestinationDraftFailureAndRecovery()
    {
        foreach (var invalidY in new[] { "unfinished", "2147483648" })
        {
            var engine = new FakePlaybackEngine();
            using var macro = new MacroViewModel("Destination drafts", engine);
            macro.AddEvent(new MouseEvent(10, 20, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 100 });
            macro.AddEvent(new MouseEvent(30, 40, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 200 });
            macro.AddEvent(new MouseEvent(50, 60, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 500000 });
            foreach (var input in macro.Events)
            {
                input.OriginalProtobufInputEvent.MergeFrom(new byte[] { 0xA0, 0x06, 0x07 });
                input.OriginalProtobufInputEvent.MouseEvent.MergeFrom(new byte[] { 0xA0, 0x06, 0x09 });
            }
            var original = macro.SnapshotBytes();
            var expected = macro.Events.Select(input => input.OriginalProtobufInputEvent.Clone()).ToArray();
            expected[1].MouseEvent.X = 123; expected[1].MouseEvent.Y = 456;
            using var editor = new MacroTabContent { DataContext = macro };
            var window = new Window { Content = editor };
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            try
            {
                Assert.IsFalse(IsWindowVisible(hwnd));
                Call(editor, "Attach"); LayoutControl(editor, 1200, 800);
                var actions = Field<ListView>(editor, "ActionsList"); actions.SelectedIndex = 0;
                var x = Field<TextBox>(editor, "DestinationX"); var y = Field<TextBox>(editor, "DestinationY");
                x.Text = "123"; y.Text = invalidY;
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    Assert.IsFalse(editor.TryCommitPendingEdits());
                    Assert.IsFalse(string.IsNullOrEmpty(editor.Status));
                    actions.SelectedItem = macro.Editor.Projection.Actions[1];
                    Assert.AreSame(macro.Events[0], ((RecordedAction)actions.SelectedItem).First);
                    Assert.AreEqual("123", x.Text); Assert.AreEqual(invalidY, y.Text);
                    CollectionAssert.AreEqual(original, macro.SnapshotBytes(), "A rejected coordinate pair must not partially change geometry or raw bytes.");
                    Assert.IsFalse(editor.CanUndo, "Failed attempts must not create undo entries.");
                }

                y.Text = "456";
                Assert.IsTrue(editor.TryCommitPendingEdits());
                for (var index = 0; index < expected.Length; index++)
                    CollectionAssert.AreEqual(expected[index].ToByteArray(), macro.Events[index].OriginalProtobufInputEvent.ToByteArray(),
                        "Changing the destination preserves timing, flags, other events, and unknown protobuf fields.");
                Assert.IsTrue(editor.TryCommitPendingEdits());
                Assert.IsTrue(editor.Undo());
                CollectionAssert.AreEqual(original, macro.SnapshotBytes());
                Assert.IsFalse(editor.CanUndo, "The corrected pair creates exactly one edit despite repeated command attempts.");
                Assert.AreEqual(0, engine.Starts);
                Assert.IsFalse(IsWindowVisible(hwnd));
            }
            finally { window.Close(); }
        }
    }

    private static async Task CheckCommittedFieldsSurviveFailedSave()
    {
        var engine = new FakePlaybackEngine(); var store = new RunTestLibrary();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), engine, store);
        var macro = vm.ActiveMacro!;
        macro.AddCaptureOrigin(new(5, 6));
        macro.AddEvent(new MouseEvent(10, 20, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 100 });
        macro.AddEvent(new MouseEvent(30, 40, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 200 });
        await vm.SaveRecordingAsync(macro);
        var original = macro.SnapshotBytes();
        var preferences = new RunPreferences(new MemoryRunPreferenceStore());
        await preferences.InitializeAsync();
        var window = new MainWindow(vm, false, preferences);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var finishSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task pending = Task.CompletedTask;
        try
        {
            SetLibraryShellField(window, "_initialized", true);
            Assert.IsFalse(IsWindowVisible(hwnd));
            var host = Field<ContentControl>(window, "EditorHost");
            var editor = (MacroTabContent)host.Content;
            Call(editor, "Attach"); LayoutControl(editor, 1200, 800);
            Field<ListView>(editor, "ActionsList").SelectedIndex = 0;
            var wait = Field<TextBox>(editor, "WaitInput"); var duration = Field<TextBox>(editor, "DurationInput");
            wait.Text = "0.012345"; duration.Text = "0.000750";
            store.BeforeSave = async () => { await finishSave.Task; throw new IOException("hidden test save failure"); };
            pending = (Task)Call(window, "OperationAsync", (Func<Task>)(() => vm.SaveRecordingAsync(macro)))!;
            Assert.IsFalse(host.IsEnabled);
            Assert.IsFalse(pending.IsCompleted);
            Assert.AreEqual(12345UL, macro.Events[0].TimeSinceLastEvent);
            Assert.AreEqual(750UL, macro.Events[1].TimeSinceLastEvent);
            var committed = macro.SnapshotBytes();
            var reentered = false;
            await (Task)Call(window, "OperationAsync", (Func<Task>)(() => { reentered = true; return Task.CompletedTask; }))!;
            Assert.IsFalse(reentered, "An in-progress save keeps subsequent commands gated.");
            finishSave.SetResult(); await pending;

            Assert.IsTrue(host.IsEnabled, "A failed save must release the editor command gate.");
            Assert.AreEqual(RecordingSaveState.Failed, macro.SaveState);
            Assert.IsTrue(macro.IsDirty);
            StringAssert.Contains(Field<TextBlock>(window, "StatusText").Text, "hidden test save failure");
            CollectionAssert.AreEqual(original, store.Records[macro.RecordingId].MacroBytes);
            CollectionAssert.AreEqual(committed, macro.SnapshotBytes(), "Failed persistence must retain the committed edits for retry.");
            Assert.AreEqual("0.012345", wait.Text); Assert.AreEqual("0.00075", duration.Text);

            store.BeforeSave = null;
            await (Task)Call(window, "OperationAsync", (Func<Task>)(() => vm.SaveRecordingAsync(macro)))!;
            Assert.AreEqual(RecordingSaveState.Saved, macro.SaveState);
            Assert.IsFalse(macro.IsDirty); Assert.IsTrue(host.IsEnabled);
            CollectionAssert.AreEqual(committed, store.Records[macro.RecordingId].MacroBytes);
            Assert.AreEqual(3, store.Saves, "Initial save, failed save, and explicit retry each run once.");
            Assert.IsTrue(editor.Undo()); Assert.IsTrue(editor.Undo());
            CollectionAssert.AreEqual(original, macro.SnapshotBytes(), "Retry must not add duplicate field edits or alter origin metadata.");
            Assert.IsFalse(editor.CanUndo);
            Assert.AreEqual(0, engine.Starts); Assert.IsFalse(IsWindowVisible(hwnd));
        }
        finally
        {
            finishSave.TrySetResult(); await pending;
            SetLibraryShellField(window, "_allowClose", true);
            window.Close();
        }
    }
}
