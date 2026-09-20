using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.Views;
using Windows.UI;

namespace MacroRecorderGUITests;

[TestClass]
public class DesignedShellTests
{
    [TestMethod]
    public void PrototypeCaptionUsesSurfaceInkAndMutedColors()
    {
        var light = TitleBarPalette.ForTheme(false, false, true);
        Assert.AreEqual(Color.FromArgb(255, 246, 247, 248), light.Background);
        Assert.AreEqual(Color.FromArgb(255, 32, 40, 50), light.Foreground);
        Assert.AreEqual(Color.FromArgb(255, 107, 116, 129), light.InactiveForeground);
        var dark = TitleBarPalette.ForTheme(true, false, true);
        Assert.AreEqual(Color.FromArgb(255, 29, 37, 49), dark.Background);
        Assert.AreEqual(Color.FromArgb(255, 237, 242, 249), dark.Foreground);
        Assert.AreEqual(Color.FromArgb(255, 164, 176, 193), dark.InactiveForeground);
    }

    [TestMethod]
    public async Task CancelledCountdownCannotReachStart()
    {
        using var lifetime = new ShellRunLifetime();
        var run = lifetime.Begin();
        var ready = new TaskCompletionSource();
        var continued = new TaskCompletionSource();
        var starts = 0;
        async Task Countdown()
        {
            ready.SetResult();
            await continued.Task;
            run.ThrowIfCancelled();
            starts++;
        }
        var countdown = Countdown();
        await ready.Task;
        lifetime.Cancel();
        continued.SetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => countdown);
        Assert.AreEqual(0, starts);
        Assert.IsTrue(lifetime.Complete(run));
    }

    [TestMethod]
    public async Task EmergencyDuringBlockedSavePreventsRunAfterSaveCompletes()
    {
        using var lifetime = new ShellRunLifetime();
        var acceptedRun = lifetime.Begin();
        var save = new TaskCompletionSource();
        var starts = 0;
        async Task StartAfterSave()
        {
            await acceptedRun.PrepareAsync(() => save.Task);
            starts++;
        }
        var start = StartAfterSave();
        lifetime.Cancel();
        save.SetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => start);
        Assert.AreEqual(0, starts);
    }

    [TestMethod]
    public void StaleCompletionDoesNotOwnTheNextController()
    {
        using var lifetime = new ShellRunLifetime();
        var oldRun = lifetime.Begin();
        Assert.IsTrue(lifetime.Complete(oldRun));
        var next = lifetime.Begin();
        Assert.IsFalse(lifetime.Owns(oldRun));
        Assert.IsFalse(lifetime.Complete(oldRun));
        Assert.IsTrue(lifetime.Owns(next));
    }

    [TestMethod]
    public void ClosingCancelsPendingRunAndPreventsAnyNewRun()
    {
        var lifetime = new ShellRunLifetime();
        var pending = lifetime.Begin();
        lifetime.Dispose();
        Assert.ThrowsExactly<OperationCanceledException>(pending.ThrowIfCancelled);
        Assert.ThrowsExactly<ObjectDisposedException>(() => lifetime.Begin());
        Assert.IsFalse(lifetime.Owns(pending));
    }

    [TestMethod]
    public void SameEditorValidationErrorIsVisibleAgainAfterSavingOrExporting()
    {
        var feedback = new ShellFeedback();
        feedback.ReportEditor("Enter a valid duration.");
        Assert.AreEqual("Enter a valid duration.", feedback.Text);
        feedback.Report("Saved recording");
        Assert.AreEqual("Saved recording", feedback.Text);
        feedback.ReportEditor("Enter a valid duration.");
        Assert.AreEqual("Enter a valid duration.", feedback.Text);
        feedback.Report("Exported recording");
        feedback.ReportEditor("Enter a valid duration.");
        Assert.AreEqual("Enter a valid duration.", feedback.Text);
    }

    [TestMethod]
    public void StoppedRecordingWithSlowSaveNeverClaimsPlaybackIsRunning()
    {
        var state = RunControllerPresentation.ForRecording(false, false, true, TimeSpan.FromSeconds(12), 42);
        Assert.AreEqual("Saving recording", state.State);
        Assert.IsFalse(state.CanStop);
        Assert.AreEqual("00:12.0", state.Clock);
        Assert.IsFalse(state.Detail.Contains("repeat"));
    }

    [TestMethod]
    [DataRow(PlaybackPhase.Idle, "Ready")]
    [DataRow(PlaybackPhase.Completed, "Finished")]
    [DataRow(PlaybackPhase.Cancelled, "Stopped")]
    [DataRow(PlaybackPhase.Failed, "Playback failed")]
    public void TerminalPlaybackStatesAreNotPresentedAsPlaying(PlaybackPhase phase, string label)
    {
        var state = RunControllerPresentation.ForPlayback(new(phase, 0, 1, TimeSpan.Zero, TimeSpan.Zero), false);
        Assert.AreEqual(label, state.State);
        Assert.IsFalse(state.CanStop);
        Assert.IsFalse(state.Detail.Contains("repeat 0"));
    }

    [TestMethod]
    public void NativeInfiniteLoopDoesNotInventAnIterationCount()
    {
        var state = RunControllerPresentation.ForPlayback(new(PlaybackPhase.Playing, 0, 0, TimeSpan.Zero, TimeSpan.FromSeconds(4), RepeatUntilStopped: true), false);
        Assert.AreEqual("Repeating until stopped", state.Detail);
        Assert.AreEqual("00:04.0", state.Clock);
        Assert.IsFalse(state.Detail.Contains("repeat 0", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(state.CanStop);
    }

    [TestMethod]
    public void MixedCoordinateThumbnailDescribesItsRepresentativeMovementFrame()
    {
        var card = new LibraryCard(Guid.NewGuid(), "Mixed capture", "", "",
        LibraryThumbnail.Create([
            new(0, 0, new(100, 200, CoordinateSpace.AbsoluteDesktop), true, 1),
            new(1, 1, new(3, 4, CoordinateSpace.RelativeCounts), true, 2),
            new(2, 2, new(9, 8, CoordinateSpace.RelativeCounts), false, 2)
        ], []));
        StringAssert.Contains(card.TraceLabel, "Relative device counts");
        StringAssert.Contains(card.TraceLabel, "starting position unknown");
        Assert.IsFalse(card.TraceLabel.Contains("pixels"));
        Assert.IsTrue(card.Thumbnail.Samples.All(sample => sample.Segment == 2));
    }
}
