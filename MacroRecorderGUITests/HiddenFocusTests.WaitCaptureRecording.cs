using System.Reflection;
using MacroRecorderGUI;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;

namespace MacroRecorderGUITests;

public sealed partial class HiddenFocusTests
{
    private sealed class ScriptedCapturePicker : IWaitTargetPicker
    {
        public int Calls;
        public Func<WaitTargetCapture> Sample = () => new(Pixel: new(-900, -150, 0x123456));
        public WaitTargetCapture Capture(WaitCaptureTarget target) { Calls++; return Sample(); }
    }
    private sealed class CaptureShellViewModel(FakeRecordingTransport transport) : MainWindowViewModel(
        new RecordEngine(transport), new FakePlaybackEngine(), new RunTestLibrary(), new FakePointerEnvironment())
    {
        protected override void InvokeDispatcher(Action action) => action();
    }

    private static async Task CheckRecordingCaptureCommands()
    {
        var transport = new FakeRecordingTransport(); using var vm = new CaptureShellViewModel(transport);
        var prefs = new RunPreferences(new MemoryRunPreferenceStore()); await prefs.InitializeAsync();
        var picker = new ScriptedCapturePicker(); var capturePrefs = new MemoryCapturePreferences();
        var registrations = new HashSet<int>();
        using var global = new GlobalHotkeys((id, _, _) => registrations.Add(id), registrations.Remove);
        var window = new MainWindow(vm, false, prefs, picker, capturePrefs);
        try
        {
            typeof(MainWindow).GetField("_globalHotkeys", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, global);
            await (Task)Call(window, "InitializeCapturePreferencesAsync")!;
            var config = WaitCaptureConfiguration.Default with { PointerPixel = WaitCaptureConfiguration.Default.PointerPixel with { Enabled = true } };
            Assert.IsNull(await (Task<string?>)Call(window, "ApplyShortcutSettingsAsync", 0, config)!);
            Assert.IsTrue(vm.StartRecording()); var id = transport.Starts.Single(); transport.Begin(id);
            var macro = vm.RecordingMacro!;
            var expectedGesture = new RecordingCaptureGesture((uint)config.PointerPixel.Modifiers, (uint)config.PointerPixel.Key);
            CollectionAssert.AreEqual(new[] { expectedGesture }, transport.CaptureGestures.Single().ToArray());
            var time = unchecked((uint)Environment.TickCount);
            Call(window, "CaptureWaitHotkey", WaitCaptureTarget.PointerPixel, time);
            Assert.AreEqual((id, expectedGesture, time), (transport.CapturedWaits.Single().Session, transport.CapturedWaits.Single().Gesture, transport.CapturedWaits.Single().Time));
            Assert.AreEqual(-900, transport.CapturedWaits.Single().Condition!.Pixel.X);
            Assert.IsEmpty(macro.Events, "Only ordered native ingress may append the wait.");
            var editor = (MacroTabContent)Field<Microsoft.UI.Xaml.Controls.ContentControl>(window, "EditorHost").Content;
            Assert.IsNull(Field<WaitConditionEditor?>(editor, "_modalConditionEditor"));

            foreach (var sample in new Func<WaitTargetCapture>[] { () => new(Error: "Fake unavailable desktop"),
                () => throw new ApplicationException("Fake unexpected sampling failure"),
                () => new(Pixel: new(1, 2, uint.MaxValue)) })
            {
                transport.CapturedWaits.Clear(); picker.Sample = sample;
                Call(window, "CaptureWaitHotkey", WaitCaptureTarget.PointerPixel, time);
                Assert.HasCount(1, transport.CapturedWaits); Assert.IsNull(transport.CapturedWaits[0].Condition);
                Assert.AreEqual((id, expectedGesture, time), (transport.CapturedWaits[0].Session, transport.CapturedWaits[0].Gesture, transport.CapturedWaits[0].Time));
                Assert.IsFalse(string.IsNullOrWhiteSpace(Field<string?>(window, "_captureRunNotice")));
            }
            transport.CapturedWaits.Clear(); transport.CaptureResult = false;
            picker.Sample = () => new(Pixel: new(-30, 5, 0x112233));
            Call(window, "CaptureWaitHotkey", WaitCaptureTarget.PointerPixel, time);
            Assert.HasCount(2, transport.CapturedWaits); Assert.IsNull(transport.CapturedWaits.Last().Condition);
            StringAssert.Contains(Field<string>(window, "_captureRunNotice"), "Stop recording");
            transport.CaptureResult = true;
            var changed = await (Task<string?>)Call(window, "ApplyShortcutSettingsAsync", 0, WaitCaptureConfiguration.Default)!;
            Assert.IsNotNull(changed); Assert.AreEqual(config, capturePrefs.Saved);
            var stop = vm.StopRecordingAsync(); transport.End(id); await stop;
            // Simulate a destination change during the synchronous sampling seam. It must not open a dialog in the replacement editor.
            picker.Sample = () => { vm.AddNewTab(); return new(Pixel: new(1, 2, 0x112233)); };
            Call(window, "CaptureWaitHotkey", WaitCaptureTarget.PointerPixel, unchecked((uint)Environment.TickCount));
            Assert.IsEmpty(vm.ActiveMacro!.Events); Assert.IsEmpty(macro.Events);
            var current = (MacroTabContent)Field<Microsoft.UI.Xaml.Controls.ContentControl>(window, "EditorHost").Content;
            Assert.IsNull(Field<WaitConditionEditor?>(current, "_modalConditionEditor"));
            Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
        }
        finally
        {
            if (vm.IsRecording) { var stop = vm.StopRecordingAsync(); transport.End(transport.Starts.Last()); await stop; }
            await window.CloseSafelyAsync(_ => Task.FromResult(false));
        }
        await CheckRejectedRecordingCaptureCommands();
    }

    private static async Task CheckRejectedRecordingCaptureCommands()
    {
        var transport = new FakeRecordingTransport(); using var vm = new CaptureShellViewModel(transport);
        var prefs = new RunPreferences(new MemoryRunPreferenceStore()); await prefs.InitializeAsync();
        var picker = new ScriptedCapturePicker(); var capturePrefs = new MemoryCapturePreferences();
        var registrations = new HashSet<int>();
        using var global = new GlobalHotkeys((id, _, _) => registrations.Add(id), registrations.Remove);
        var window = new MainWindow(vm, false, prefs, picker, capturePrefs);
        try
        {
            typeof(MainWindow).GetField("_globalHotkeys", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, global);
            await (Task)Call(window, "InitializeCapturePreferencesAsync")!;
            var config = WaitCaptureConfiguration.Default with
            {
                PointerPixel = WaitCaptureConfiguration.Default.PointerPixel with { Enabled = true },
                HoveredWindow = WaitCaptureConfiguration.Default.HoveredWindow with { Enabled = true }
            };
            Assert.IsNull(await (Task<string?>)Call(window, "ApplyShortcutSettingsAsync", 0, config)!);
            var gesture = new RecordingCaptureGesture((uint)config.PointerPixel.Modifiers, (uint)config.PointerPixel.Key);
            // Deliberately model an obsolete registered callback absent from the
            // immutable native session snapshot; normal Settings cannot create this drift.
            vm.RegisteredRecordingCaptures = [gesture];
            var staleTime = unchecked((uint)Environment.TickCount - 1);
            Assert.IsTrue(vm.StartRecording()); var id = transport.Starts.Single(); transport.Begin(id);
            var time = unchecked((uint)Environment.TickCount);
            Call(window, "CaptureWaitHotkey", WaitCaptureTarget.PointerPixel, time);
            var currentMarker = transport.CapturedWaits.Single();
            Assert.IsNotNull(currentMarker.Condition);
            picker.Calls = 0;
            Call(window, "CaptureWaitHotkey", WaitCaptureTarget.PointerPixel, staleTime);
            Call(window, "CaptureWaitHotkey", WaitCaptureTarget.HoveredWindow, time);
            Assert.AreEqual(0, picker.Calls, "Rejected preflight must precede all target observation.");
            Assert.HasCount(1, transport.CapturedWaits, "Neither rejected callback may submit or cancel the current marker.");
            Assert.AreEqual(currentMarker, transport.CapturedWaits.Single());
            Assert.IsEmpty(vm.RecordingMacro!.Events);
            Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
        }
        finally
        {
            if (vm.IsRecording) { var stop = vm.StopRecordingAsync(); transport.End(transport.Starts.Last()); await stop; }
            await window.CloseSafelyAsync(_ => Task.FromResult(false));
        }
    }
}
