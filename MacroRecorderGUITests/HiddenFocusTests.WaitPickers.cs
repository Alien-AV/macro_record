using Google.Protobuf;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Views;
using Microsoft.UI.Xaml.Controls;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

public sealed partial class HiddenFocusTests
{
    private sealed class FakeWaitPicker : IWaitTargetPicker
    {
        public int Calls;
        public WaitTargetCapture Capture(WaitCaptureTarget target)
        { Calls++; return new(Window: new() { ExecutablePath = @"C:\Test.exe", WindowClass = "Test", Title = "Captured" }); }
    }

    private static async Task CheckWaitPickerControls()
    {
        var template = ConditionalWaitTests.Condition();
        // Field 100 is intentionally unknown at each level, as with a newer document.
        template.MergeFrom(new byte[] { 0xa0, 0x06, 0x29 });
        template.Window.MergeFrom(new byte[] { 0xa0, 0x06, 0x2a });
        template.Window.Target.MergeFrom(new byte[] { 0xa0, 0x06, 0x2b });
        template.Trigger = WaitTrigger.BecomesTrue;
        var observer = new HiddenWaitObserver(); var picker = new FakeWaitPicker();
        using var fields = new WaitConditionEditor(template, new WaitRunner(observer), picker, (_, _) => Task.CompletedTask);
        var original = template.ToByteArray();
        fields.ApplyWindowCapture(new() { ExecutablePath = @"C:\New.exe", WindowClass = "New", Title = "Picked" });
        var picked = fields.Read();
        Assert.AreEqual(WaitTrigger.BecomesTrue, picked.Trigger); Assert.AreEqual(template.TimeoutUs, picked.TimeoutUs);
        Assert.AreEqual(template.Window.Test, picked.Window.Test); Assert.AreEqual("Picked", picked.Window.Target.Title);
        var expected = template.Clone(); expected.Window.Target.ExecutablePath = @"C:\New.exe";
        expected.Window.Target.WindowClass = "New"; expected.Window.Target.Title = "Picked";
        expected.Window.Target.TitleMatch = TitleMatch.Exact; expected.Window.Target.IgnoreTitleCase = false;
        CollectionAssert.AreEqual(expected.ToByteArray(), picked.ToByteArray());
        CollectionAssert.AreEqual(original, template.ToByteArray());
        fields.Timeout.Text = "unfinished";
        await fields.PickAsync(new Button(), "Pick", WaitCaptureTarget.HoveredWindow, capture => fields.ApplyWindowCapture(capture.Window!));
        Assert.AreEqual("unfinished", fields.Timeout.Text); Assert.AreEqual("Captured", fields.Title.Text);
        Assert.AreEqual(1, picker.Calls); Assert.AreEqual(0, observer.Calls);

        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pending = new WaitConditionEditor(template, new WaitRunner(observer), picker, (_, _) => blocked.Task);
        var task = pending.PickAsync(new Button(), "Pick", WaitCaptureTarget.HoveredWindow, capture => pending.ApplyWindowCapture(capture.Window!));
        pending.CancelTest();
        await task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, picker.Calls); Assert.AreEqual(template.Window.Target.Title, pending.Title.Text);
        var second = pending.PickAsync(new Button(), "Pick", WaitCaptureTarget.HoveredWindow, capture => pending.ApplyWindowCapture(capture.Window!));
        pending.Dispose(); blocked.TrySetResult(); await second.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, picker.Calls); Assert.AreEqual(0, observer.Calls);

        var pixelTemplate = template.Clone(); pixelTemplate.Pixel = new() { X = 2, Y = 3, Rgb = 0xabcdef, Tolerance = 7, NotEqual = true };
        pixelTemplate.Pixel.MergeFrom(new byte[] { 0xa0, 0x06, 0x2c });
        using var pixel = new WaitConditionEditor(pixelTemplate, new WaitRunner(observer), picker);
        pixel.ApplyPixelCapture(new(Pixel: new(-500, -40, 0x0011ff)));
        var result = pixel.Read(); var pixelExpected = pixelTemplate.Clone();
        pixelExpected.Pixel.X = -500; pixelExpected.Pixel.Y = -40; pixelExpected.Pixel.Rgb = 0x0011ff;
        CollectionAssert.AreEqual(pixelExpected.ToByteArray(), result.ToByteArray());
        Assert.AreEqual(0, observer.Calls);
    }
}
