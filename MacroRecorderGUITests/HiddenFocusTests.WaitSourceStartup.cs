using MacroRecorder.Waiting;
using MacroRecorderGUI;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;

namespace MacroRecorderGUITests;

public sealed partial class HiddenFocusTests
{
    private sealed class DeferredSourcePreferenceStore : IWaitSourcePreferenceStore
    {
        public int Reads;
        public readonly TaskCompletionSource<WaitSourcePreferenceData> Loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WaitSourcePreferenceData> LoadAsync(CancellationToken token) { Reads++; return Loaded.Task.WaitAsync(token); }
        public Task SaveAsync(WaitLocalOptions options, CancellationToken token) => throw new AssertFailedException("Startup cannot write source preferences.");
    }

    private static async Task CheckWaitSourceStartup()
    {
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), new RunTestLibrary());
        var prefs = new RunPreferences(new MemoryRunPreferenceStore()); await prefs.InitializeAsync();
        var store = new DeferredSourcePreferenceStore(); var settings = new WaitLocalSettings();
        var sourcePreferences = new WaitSourcePreferences(store, settings);
        var window = new MainWindow(vm, false, prefs, sourcePreferences: sourcePreferences);
        try
        {
            Assert.AreEqual(0, store.Reads, "Constructing hidden controls cannot read a real source-preferences store.");
            var ran = false;
            await (Task)Call(window, "OperationAsync", (Func<Task>)(() => { ran = true; return Task.CompletedTask; }))!;
            Assert.IsFalse(ran);
            var initializing = (Task)Call(window, "InitializeWaitSourcePreferencesAsync")!;
            Assert.AreEqual(1, store.Reads); Assert.IsFalse(initializing.IsCompleted);
            await window.ExecuteLibraryCommandAsync(Guid.NewGuid(), playback: true, (_, _, _) => { ran = true; return Task.CompletedTask; });
            Assert.IsFalse(ran, "Library playback must share the startup gate.");
            store.Loaded.SetResult(new(new(), "Fake source-settings warning")); await initializing;
            await (Task)Call(window, "OperationAsync", (Func<Task>)(() => { ran = true; return Task.CompletedTask; }))!;
            Assert.IsTrue(ran); Assert.IsFalse(settings.Options.MemoryEnabled);
            StringAssert.Contains(Field<Microsoft.UI.Xaml.Controls.TextBlock>(window, "PreferenceWarning").Text, "Fake source-settings warning");
            Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
        }
        finally { await window.CloseSafelyAsync(_ => Task.FromResult(false)); }
    }
}
