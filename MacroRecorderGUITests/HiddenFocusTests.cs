using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MacroRecorderGUI;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class HiddenFocusTests
{
    // WinUI needs an STA entry point and its own lifetime, outside the VSTest host.
    [TestMethod]
    [DoNotParallelize]
    public async Task HiddenControlsCommitBeforeCommandsAndPreserveDraftsAndSelection()
    {
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "MacroRecorderGUITests.exe"), "--hidden-focus-check")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch { if (!process.HasExited) process.Kill(); throw; }
        Assert.AreEqual(0, process.ExitCode, await output + await errors);
    }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args is not ["--hidden-focus-check"]) return 2;
        var completion = new TaskCompletionSource();
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                new TestApp(completion);
            });
            completion.Task.GetAwaiter().GetResult();
            Console.WriteLine("Hidden controls passed; no window shown or activated. Routed focus and physical pointer gestures require an interactive smoke test.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private sealed class TestApp(TaskCompletionSource completion) : App
    {
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            DispatcherQueue.GetForCurrentThread().TryEnqueue(async () =>
            {
                try { await CheckControls(); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { Exit(); }
            });
        }
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    private static object? Call(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, args);

    private static async Task CheckControls()
    {
        var engine = new FakePlaybackEngine(); var store = new RunTestLibrary();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), engine, store);
        var macro = vm.ActiveMacro!;
        macro.AddEvent(new MouseEvent(10, 20, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 100 });
        macro.AddEvent(new MouseEvent(30, 40, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 200 });
        macro.AddEvent(new MouseEvent(50, 60, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 500000 });
        var preferences = new RunPreferences(new MemoryRunPreferenceStore());
        await preferences.InitializeAsync();
        var window = new MainWindow(vm, false, preferences);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        try
        {
            Assert.IsFalse(IsWindowVisible(hwnd));
            typeof(MainWindow).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
            var root = Field<Grid>(window, "RootGrid");
            root.Measure(new Windows.Foundation.Size(1200, 800));
            root.Arrange(new Windows.Foundation.Rect(0, 0, 1200, 800)); root.UpdateLayout();
            var host = Field<ContentControl>(window, "EditorHost");
            var editor = (MacroTabContent)host.Content;
            Call(editor, "Attach");
            var wait = Field<TextBox>(editor, "WaitInput"); var duration = Field<TextBox>(editor, "DurationInput");
            var x = Field<TextBox>(editor, "DestinationX"); var y = Field<TextBox>(editor, "DestinationY");
            var actions = Field<ListView>(editor, "ActionsList");

            wait.Text = "0.012345"; duration.Text = "bad";
            Assert.IsFalse(editor.TryCommitPendingEdits());
            Assert.AreEqual(12345UL, macro.Events[0].TimeSinceLastEvent);
            Assert.AreEqual("bad", duration.Text); Assert.IsFalse(string.IsNullOrEmpty(editor.Status));
            var error = editor.Status;
            actions.SelectedItem = macro.Editor.Projection.Actions[1];
            Assert.AreSame(macro.Events[0], ((MacroRecorderGUI.Editor.RecordedAction)actions.SelectedItem).First);
            Assert.AreEqual("bad", duration.Text); Assert.AreEqual(error, editor.Status);
            var invoked = false;
            await (Task)Call(window, "OperationAsync", (Func<Task>)(() => { invoked = true; return Task.CompletedTask; }))!;
            Assert.IsFalse(invoked); Assert.IsTrue(host.IsEnabled); Assert.AreEqual(error, editor.Status);

            duration.Text = "0.000750"; x.Text = "90"; y.Text = "100";
            await (Task)Call(window, "OperationAsync", (Func<Task>)(async () =>
            {
                Assert.IsFalse(host.IsEnabled, "Commands disable the editor only after committing all drafts.");
                Assert.AreEqual(750UL, macro.Events[1].TimeSinceLastEvent);
                Assert.AreEqual(90, ((MouseEvent)macro.Events[1]).X); Assert.AreEqual(100, ((MouseEvent)macro.Events[1]).Y);
                await vm.SaveRecordingAsync(macro);
                await engine.PlaybackEventsAsync(macro.Events);
            }))!;
            Assert.AreEqual(1, store.Saves); Assert.AreEqual(1, engine.Starts);
            Assert.IsTrue(host.IsEnabled);

            Call(editor, "SetRawOpen", true);
            var rawDelay = Field<TextBox>(editor, "RawDelay");
            rawDelay.Text = "999999";
            wait.Text = "0.05"; Assert.IsTrue(editor.TryCommitPendingEdits());
            Assert.AreEqual("999999", rawDelay.Text, "Auto-commit must not discard an explicit raw draft.");
            Assert.AreEqual(50000UL, macro.Events[0].TimeSinceLastEvent, "Raw input must not implicitly Apply.");

            wait.Text = "0.07";
            actions.SelectedItem = macro.Editor.Projection.Actions[1];
            Assert.AreEqual(70000UL, macro.Events[0].TimeSinceLastEvent);
            Assert.AreSame(macro.Events[2], ((MacroRecorderGUI.Editor.RecordedAction)actions.SelectedItem).First);
            Assert.AreEqual(500000UL, macro.Events[2].TimeSinceLastEvent);

            actions.SelectedItem = macro.Editor.Projection.Actions[0];
            var selectionChanges = 0;
            actions.SelectionChanged += (_, _) => selectionChanges++;
            actions.SelectedItems.Add(macro.Editor.Projection.Actions[1]);
            Assert.AreEqual(2, actions.SelectedItems.Count);
            Assert.AreEqual(1, selectionChanges, "Ordinary multi-selection must not be cleared and rebuilt, which resets the keyboard selection anchor.");

            CheckPointerPolicy();
            Assert.IsFalse(IsWindowVisible(hwnd));
        }
        finally
        {
            typeof(MainWindow).GetField("_allowClose", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
            window.Close();
        }
    }

    private static void CheckPointerPolicy()
    {
        var root = new StackPanel();
        var text = new TextBox(); var number = new NumberBox(); var button = new Button();
        var label = new TextBlock(); var selectable = new TextBlock { IsTextSelectionEnabled = true };
        // The unactivated tree has no visual ancestry. Supply routes to the same policy
        // used by the adapter; real pointer routing remains an interactive smoke check.
        Assert.IsTrue(ClickAwayFocus.IsBackgroundPress(root, new DependencyObject[] { root }, true, false));
        Assert.IsTrue(ClickAwayFocus.IsBackgroundPress(root, new DependencyObject[] { label, root }, true, false));
        foreach (var child in new DependencyObject[] { text, number, button, selectable })
            Assert.IsFalse(ClickAwayFocus.IsBackgroundPress(root, new DependencyObject[] { label, child, root }, true, false));
        Assert.IsFalse(ClickAwayFocus.IsBackgroundPress(root, new DependencyObject[] { root }, false, false));
        Assert.IsFalse(ClickAwayFocus.IsBackgroundPress(root, new DependencyObject[] { root }, true, true));
        Assert.IsFalse(ClickAwayFocus.IsBackgroundPress(root, new DependencyObject[] { new TextBlock() }, true, false));
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}
