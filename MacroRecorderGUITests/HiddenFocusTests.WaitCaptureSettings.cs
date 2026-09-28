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
    }
}
