using System.Reflection;
using MacroRecorderGUI;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;

namespace MacroRecorderGUITests;

public sealed partial class HiddenFocusTests
{
    private sealed class MemoryCapturePreferences : IWaitCapturePreferenceStore
    {
        public WaitCaptureConfiguration Saved = WaitCaptureConfiguration.Default;
        public bool FailSave;
        public TaskCompletionSource? PendingSave;
        public int Reads, Writes;
        public Task<WaitCapturePreferenceData> LoadAsync(CancellationToken token = default)
        { Reads++; return Task.FromResult(new WaitCapturePreferenceData(Saved)); }
        public async Task SaveAsync(WaitCaptureConfiguration configuration, CancellationToken token = default)
        {
            Writes++;
            if (PendingSave is { } pending) await pending.Task.WaitAsync(token);
            if (FailSave) throw new IOException("Fake preference write failed"); Saved = configuration;
        }
    }

    private static async Task CheckWaitCaptureSettings()
    {
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), new RunTestLibrary());
        var preferences = new RunPreferences(new MemoryRunPreferenceStore()); await preferences.InitializeAsync();
        var capturePreferences = new MemoryCapturePreferences(); var picker = new FakeWaitPicker();
        var registrations = new Dictionary<int, HotkeyGesture>();
        using var global = new GlobalHotkeys((id, key, modifiers) => { registrations.Add(id, new(key, modifiers)); return true; }, registrations.Remove);
        var window = new MainWindow(vm, false, preferences, picker, capturePreferences);
        try
        {
            typeof(MainWindow).GetField("_globalHotkeys", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, global);
            await (Task)Call(window, "InitializeCapturePreferencesAsync")!;
            Assert.AreEqual(1, capturePreferences.Reads); Assert.IsEmpty(registrations);
            var desired = WaitCaptureConfiguration.Default with { PointerPixel = WaitCaptureConfiguration.Default.PointerPixel with { Enabled = true } };
            var error = await (Task<string?>)Call(window, "ApplyShortcutSettingsAsync", 0, desired)!;
            Assert.IsNull(error); Assert.AreEqual(desired, capturePreferences.Saved); Assert.AreEqual(1, vm.RegisteredRecordingCaptures.Count);
            Assert.HasCount(2, registrations); // One capture plus the emergency stop.
            var editor = Field<Microsoft.UI.Xaml.Controls.ContentControl>(window, "EditorHost").Content as MacroTabContent;
            editor!.IsPreviewMode = true;
            Call(window, "CaptureWaitHotkey", WaitCaptureTarget.PointerPixel, 100u);
            Assert.AreEqual(0, picker.Calls);
            editor.IsPreviewMode = false;
            typeof(MainWindow).GetField("_preparingRun", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            error = await (Task<string?>)Call(window, "ApplyShortcutSettingsAsync", 0, WaitCaptureConfiguration.Default)!;
            Assert.IsNotNull(error); Assert.AreEqual(1, capturePreferences.Writes);
            Call(window, "CaptureWaitHotkey", WaitCaptureTarget.PointerPixel, 101u); Assert.AreEqual(0, picker.Calls);
            typeof(MainWindow).GetField("_preparingRun", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
            capturePreferences.FailSave = true;
            error = await (Task<string?>)Call(window, "ApplyShortcutSettingsAsync", 0, WaitCaptureConfiguration.Default)!;
            StringAssert.Contains(error!, "Previous settings remain active"); Assert.AreEqual(desired, capturePreferences.Saved);
            Assert.AreEqual(desired, Field<WaitCaptureHotkeys>(window, "_captureHotkeys").Configuration);
            Assert.AreEqual(1, vm.RegisteredRecordingCaptures.Count); Assert.HasCount(2, registrations);
            capturePreferences.FailSave = false;
            error = await (Task<string?>)Call(window, "ApplyShortcutSettingsAsync", 0, WaitCaptureConfiguration.Default)!;
            Assert.IsNull(error); Assert.IsEmpty(vm.RegisteredRecordingCaptures); Assert.HasCount(1, registrations);
            Call(window, "CaptureWaitHotkey", WaitCaptureTarget.PointerPixel, 102u); Assert.AreEqual(0, picker.Calls);
            capturePreferences.PendingSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var saving = (Task<string?>)Call(window, "ApplyShortcutSettingsAsync", 0, desired)!;
            Assert.IsFalse(saving.IsCompleted);
            Assert.IsFalse((bool)Call(window, "PrepareRecordingCaptureBindings")!);
            var ran = false;
            await (Task)Call(window, "OperationAsync", (Func<Task>)(() => { ran = true; return Task.CompletedTask; }))!;
            Assert.IsFalse(ran);
            Call(window, "CaptureWaitHotkey", WaitCaptureTarget.PointerPixel, 103u); Assert.AreEqual(0, picker.Calls);
            capturePreferences.PendingSave.SetResult(); Assert.IsNull(await saving);
            Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
        }
        finally { await window.CloseSafelyAsync(_ => Task.FromResult(false)); }
        Assert.IsEmpty(registrations);
        await CheckCaptureSaveRollbackFailure();
        await CheckCaptureSaveFailureAfterRejectedClose();
    }

    private static async Task CheckCaptureSaveRollbackFailure()
    {
        foreach (var previouslyEnabled in new[] { false, true })
        {
            using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), new RunTestLibrary());
            var preferences = new RunPreferences(new MemoryRunPreferenceStore()); await preferences.InitializeAsync();
            var previous = WaitCaptureConfiguration.Default with
            { PointerPixel = WaitCaptureConfiguration.Default.PointerPixel with { Enabled = previouslyEnabled } };
            var desired = previous with { PointerPixel = previous.PointerPixel with { Enabled = true, Key = Windows.System.VirtualKey.F9 } };
            var capturePreferences = new MemoryCapturePreferences { Saved = previous };
            var picker = new FakeWaitPicker(); var registrations = new Dictionary<int, HotkeyGesture>();
            var refuseNewRelease = false;
            using var global = new GlobalHotkeys(
                (id, key, modifiers) => { registrations.Add(id, new(key, modifiers)); return true; },
                id => !(refuseNewRelease && registrations[id] == desired.PointerPixel.Gesture) && registrations.Remove(id));
            var window = new MainWindow(vm, false, preferences, picker, capturePreferences);
            try
            {
                typeof(MainWindow).GetField("_globalHotkeys", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, global);
                await (Task)Call(window, "InitializeCapturePreferencesAsync")!;
                capturePreferences.FailSave = true;
                capturePreferences.PendingSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var saving = (Task<string?>)Call(window, "ApplyShortcutSettingsAsync", 0, desired)!;
                Assert.IsFalse(saving.IsCompleted);
                Assert.AreEqual(desired.PointerPixel.Gesture, registrations.Single().Value);
                refuseNewRelease = true; capturePreferences.PendingSave.SetResult();
                StringAssert.Contains((await saving)!, "Capture is disabled");
                var keys = Field<WaitCaptureHotkeys>(window, "_captureHotkeys");
                Assert.IsFalse(keys.IsReady); Assert.IsTrue(keys.HasRegistrations);
                Assert.IsEmpty(keys.RegisteredBindings); Assert.IsEmpty(vm.RegisteredRecordingCaptures);
                Assert.AreEqual(previous, capturePreferences.Saved);
                Assert.AreEqual(previous, Field<WaitCaptureConfiguration>(window, "_captureConfiguration"));
                Assert.IsFalse((bool)Call(window, "PrepareRecordingCaptureBindings")!);
                var time = unchecked((uint)Environment.TickCount + 1);
                Assert.IsTrue(global.DispatchHotkey(registrations.Single().Key, time));
                Call(window, "CaptureWaitHotkey", WaitCaptureTarget.PointerPixel, time);
                Assert.AreEqual(0, picker.Calls, "Failed restoration must leave no dispatch path armed.");

                Assert.IsNotNull(await (Task<string?>)Call(window, "ApplyShortcutSettingsAsync", 0, previous)!);
                Assert.AreEqual(1, capturePreferences.Writes, "Failed cleanup must precede another preference write.");
                Assert.IsFalse(keys.IsReady); Assert.HasCount(1, registrations);
                refuseNewRelease = false; capturePreferences.FailSave = false; capturePreferences.PendingSave = null;
                Assert.IsNull(await (Task<string?>)Call(window, "ApplyShortcutSettingsAsync", 0, previous)!);
                Assert.IsTrue(keys.IsReady); Assert.AreEqual(previous, keys.Configuration);
                Assert.AreEqual(previouslyEnabled ? 1 : 0, vm.RegisteredRecordingCaptures.Count);
                Assert.IsFalse(registrations.Values.Contains(desired.PointerPixel.Gesture));
                Assert.IsTrue((bool)Call(window, "PrepareRecordingCaptureBindings")!);
                Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
            }
            finally { refuseNewRelease = false; await window.CloseSafelyAsync(_ => Task.FromResult(false)); }
            Assert.IsEmpty(registrations);
        }
    }

    private static async Task CheckCaptureSaveFailureAfterRejectedClose()
    {
        foreach (var previouslyEnabled in new[] { false, true })
        {
            var library = new RunTestLibrary();
            using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), library);
            var preferences = new RunPreferences(new MemoryRunPreferenceStore()); await preferences.InitializeAsync();
            var previous = WaitCaptureConfiguration.Default with
            { PointerPixel = WaitCaptureConfiguration.Default.PointerPixel with { Enabled = previouslyEnabled } };
            var desired = previous with { PointerPixel = previous.PointerPixel with { Enabled = true, Key = Windows.System.VirtualKey.F9 } };
            var capturePreferences = new MemoryCapturePreferences { Saved = previous, FailSave = true,
                PendingSave = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            var picker = new FakeWaitPicker(); var registrations = new Dictionary<int, HotkeyGesture>();
            using var global = new GlobalHotkeys(
                (id, key, modifiers) => { registrations.Add(id, new(key, modifiers)); return true; }, registrations.Remove);
            var window = new MainWindow(vm, false, preferences, picker, capturePreferences);
            var closeFailure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var keepOpen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<string?>? saving = null; Task? closing = null;
            try
            {
                typeof(MainWindow).GetField("_globalHotkeys", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, global);
                await (Task)Call(window, "InitializeCapturePreferencesAsync")!;
                saving = (Task<string?>)Call(window, "ApplyShortcutSettingsAsync", 0, desired)!;
                Assert.IsFalse(saving.IsCompleted);
                vm.ActiveMacro!.AddEvent(new MacroRecorderGUI.Event.DelayEvent(1));
                library.BeforeSave = () => Task.FromException(new IOException("Fake document save failure during close"));
                closing = window.CloseSafelyAsync(error => { closeFailure.TrySetResult(error); return keepOpen.Task; });
                StringAssert.Contains(await closeFailure.Task.WaitAsync(TimeSpan.FromSeconds(3)), "Fake document save failure");
                Assert.IsTrue(Field<bool>(window, "_closing"));
                capturePreferences.PendingSave.SetResult();
                StringAssert.Contains((await saving)!, "Capture is disabled");
                var keys = Field<WaitCaptureHotkeys>(window, "_captureHotkeys");
                Assert.IsFalse(keys.IsReady); Assert.IsTrue(keys.HasRegistrations);
                Assert.IsEmpty(vm.RegisteredRecordingCaptures);
                Assert.AreEqual(previous, capturePreferences.Saved);
                Assert.AreEqual(previous, Field<WaitCaptureConfiguration>(window, "_captureConfiguration"));

                keepOpen.SetResult(false); await closing;
                Assert.IsFalse(Field<bool>(window, "_closing")); Assert.IsFalse(Field<bool>(window, "_closed"));
                Assert.IsFalse(keys.IsReady, "Keep open must not rearm an unsaved configuration.");
                Assert.IsFalse((bool)Call(window, "PrepareRecordingCaptureBindings")!);
                var time = unchecked((uint)Environment.TickCount + 1);
                Assert.IsTrue(global.DispatchHotkey(registrations.Single().Key, time));
                Call(window, "CaptureWaitHotkey", WaitCaptureTarget.PointerPixel, time);
                Assert.AreEqual(0, picker.Calls);
                Assert.AreEqual(desired.PointerPixel.Gesture, registrations.Single().Value,
                    "The disabled registration remains owned for cleanup, not forgotten.");
                capturePreferences.FailSave = false; capturePreferences.PendingSave = null;
                Assert.IsNull(await (Task<string?>)Call(window, "ApplyShortcutSettingsAsync", 0, previous)!);
                Assert.IsTrue(keys.IsReady); Assert.AreEqual(previous, keys.Configuration);
                Assert.AreEqual(previouslyEnabled ? 1 : 0, vm.RegisteredRecordingCaptures.Count);
                Assert.IsFalse(registrations.Values.Contains(desired.PointerPixel.Gesture));
                Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
            }
            finally
            {
                capturePreferences.PendingSave?.TrySetResult(); keepOpen.TrySetResult(false);
                if (saving is not null) await saving;
                if (closing is not null) await closing;
                library.BeforeSave = null; await window.CloseSafelyAsync(_ => Task.FromResult(false));
            }
            Assert.IsEmpty(registrations);
        }
    }
}
