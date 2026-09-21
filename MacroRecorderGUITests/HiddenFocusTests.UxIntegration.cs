using System.Reflection;
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
    private static async Task CheckUxLibraryEditorIntegration()
    {
        var engine = new FakePlaybackEngine();
        var store = new RunTestLibrary();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), engine, store);
        var source = await vm.CreateDraftAsync("Open editor");
        source.AddEvent(new MouseEvent(10, 20, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 100 });
        source.AddEvent(new WaitConditionEvent(ConditionalWaitTests.Condition()));
        var target = await vm.CreateDraftAsync("Card target");
        target.AddEvent(new MouseEvent(30, 40, MouseActionTypeFlags.Move));
        await vm.SaveRecordingAsync(target);
        vm.SelectedTabIndex = vm.MacroTabs.IndexOf(source);
        var preferences = new RunPreferences(new MemoryRunPreferenceStore());
        var options = new PlaybackOptions { Speed = 2, RepeatCount = 7, Countdown = TimeSpan.FromSeconds(4), PointerOrigin = PlaybackPointerOrigin.CurrentPointer };
        await preferences.SavePlaybackAsync(target.RecordingId, options, default);
        var window = new MainWindow(vm, false, preferences);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        try
        {
            typeof(MainWindow).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
            Assert.IsFalse(IsWindowVisible(hwnd));
            var editor = (MacroTabContent)Field<ContentControl>(window, "EditorHost").Content;
            Call(editor, "Attach"); LayoutControl(editor, 1200, 800);
            var actions = Field<ListView>(editor, "ActionsList"); actions.SelectedIndex = 0;
            Call(editor, "SetRawOpen", true);
            var rawDelay = Field<TextBox>(editor, "RawDelay");
            var rawSelection = Field<ListView>(editor, "RawList").SelectedItem;
            var original = source.SnapshotBytes();
            rawDelay.Text = "246";
            Call(editor, "SetRawOpen", false);
            LayoutControl(editor, 420, 300);
            LayoutControl(editor, 1200, 800);
            var invoked = 0;
            var handoffs = new List<(MacroViewModel Target, MacroViewModel? Active, PlaybackOptions Options, bool Cancelled, bool Visible, RunController? Controller)>();
            Task Handoff(MacroViewModel macro, PlaybackOptions actual, RunLease lease)
            {
                invoked++;
                handoffs.Add((macro, vm.ActiveMacro, actual, lease.Token.IsCancellationRequested,
                    IsWindowVisible(hwnd), Field<RunController?>(window, "_controller")));
                return Task.CompletedTask;
            }
            void CheckHandoffs()
            {
                // Assert outside OperationAsync: its production catch-all reports callback errors.
                Assert.AreEqual(invoked, handoffs.Count);
                foreach (var handoff in handoffs)
                {
                    Assert.AreSame(target, handoff.Target);
                    Assert.AreSame(source, handoff.Active);
                    Assert.AreEqual(options, handoff.Options);
                    Assert.IsFalse(handoff.Cancelled);
                    Assert.IsFalse(handoff.Visible);
                    Assert.IsNull(handoff.Controller);
                }
            }
            await window.ExecuteLibraryCommandAsync(target.RecordingId, true, Handoff);
            await window.ExecuteLibraryCommandAsync(target.RecordingId, false, Handoff);
            Assert.AreEqual(0, invoked, "Hidden raw drafts must gate both card Play and options, even after returning to Details.");
            CollectionAssert.AreEqual(original, source.SnapshotBytes());
            Call(editor, "SetRawOpen", true);
            Assert.AreEqual("246", rawDelay.Text);
            Assert.AreSame(rawSelection, Field<ListView>(editor, "RawList").SelectedItem);
            Call(editor, "RawApply_Click", editor, new RoutedEventArgs());
            Assert.AreEqual(246ul, source.Events[0].TimeSinceLastEvent);
            Call(editor, "SetRawOpen", false);
            await window.ExecuteLibraryCommandAsync(target.RecordingId, true, Handoff);
            Assert.AreEqual(1, invoked);
            CheckHandoffs();
            Assert.AreEqual(0, engine.Starts, "The injected callback never enters the real controller/execution path.");

            source.Editor.Refresh(); actions.SelectedItem = source.Editor.Projection.ActionAt(1);
            var condition = Field<WaitConditionEditor>(editor, "_conditionEditor");
            condition.Timeout.Text = "not a timeout";
            await window.ExecuteLibraryCommandAsync(target.RecordingId, false, Handoff);
            Assert.AreEqual(1, invoked);
            Assert.AreEqual("not a timeout", condition.Timeout.Text);
            condition.Timeout.Text = "31";
            Assert.IsTrue(editor.TryCommitPendingEdits());
            using var modal = new WaitConditionEditor(ConditionalWaitTests.Condition(), new WaitRunner(new HiddenWaitObserver()));
            Call(editor, "BeginWaitDialog", modal);
            try
            {
                await window.ExecuteLibraryCommandAsync(target.RecordingId, true, Handoff);
                Assert.AreEqual(1, invoked, "A modal authoring/test owner must gate card commands too.");
            }
            finally { Call(editor, "EndWaitDialog", modal); }
            await window.ExecuteLibraryCommandAsync(target.RecordingId, false, Handoff);
            Assert.AreEqual(2, invoked);
            CheckHandoffs();
            Assert.AreSame(source, vm.ActiveMacro);
            Assert.AreEqual(0, engine.Starts);
            Assert.IsFalse(IsWindowVisible(hwnd));
            Assert.IsNull(Field<RunController?>(window, "_controller"));
        }
        finally
        {
            typeof(MainWindow).GetField("_allowClose", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
            window.Close();
        }
    }
}
