using System.Runtime.CompilerServices;
using System.Xml.Linq;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Views;

namespace MacroRecorderGUITests;

[TestClass]
public class RunSurfacePolishTests
{
    [TestMethod]
    public void CountdownsRoundUpAndRemainCancellableBeforeAnyInput()
    {
        var recording = RunControllerPresentation.RecordingCountdown(2.1);
        Assert.AreEqual("3", recording.Clock);
        Assert.IsTrue(recording.Countdown && recording.CanStop && recording.Recording);
        StringAssert.Contains(recording.Note, "No input is being recorded");

        var playback = RunControllerPresentation.ForPlayback(new(PlaybackPhase.Countdown, 0, 7,
            TimeSpan.FromSeconds(2.1), TimeSpan.Zero), false);
        Assert.AreEqual("3", playback.Clock);
        Assert.IsTrue(playback.Countdown && playback.CanStop);
        Assert.IsFalse(playback.Recording);
        StringAssert.Contains(playback.Note, "No input yet");
        StringAssert.Contains(playback.Note, "focused app");
        Assert.IsFalse(playback.Detail.Contains("Repeat"));
    }

    [TestMethod]
    public void PlaybackShowsOnlyObservedRepeatsAndNeverEstimatedCompletion()
    {
        var single = RunControllerPresentation.ForPlayback(new(PlaybackPhase.Playing, 1, 1,
            TimeSpan.Zero, TimeSpan.FromMilliseconds(3_661_250)), false);
        Assert.AreEqual("61:01.2", single.Clock, "Elapsed minutes must not wrap at an hour.");
        Assert.AreEqual("", single.Detail, "A single run does not need repeat bookkeeping.");
        StringAssert.Contains(single.Note, "Sending input to the focused app");

        var repeated = RunControllerPresentation.ForPlayback(new(PlaybackPhase.Playing, 2, 7,
            TimeSpan.Zero, TimeSpan.FromSeconds(12)), false);
        Assert.AreEqual("Repeat 2 of 7", repeated.Detail);
        Assert.AreEqual("00:12.0", repeated.Clock);
        Assert.IsTrue(repeated.CanStop);

        var unknown = RunControllerPresentation.ForPlayback(new(PlaybackPhase.Playing, 0, 0,
            TimeSpan.Zero, TimeSpan.FromSeconds(12)), false);
        Assert.AreEqual("", unknown.Detail);
        Assert.IsFalse(unknown.Note.Contains('%'));
    }

    [TestMethod]
    public void StoppingNeverPromisesThatInputHasAlreadyStopped()
    {
        var state = RunControllerPresentation.ForPlayback(new(PlaybackPhase.Stopping, 0, 0,
            TimeSpan.Zero, TimeSpan.FromSeconds(9), RepeatUntilStopped: true), false);
        Assert.AreEqual("Stopping…", state.State);
        Assert.IsTrue(state.CanStop);
        StringAssert.Contains(state.Note, "Input may continue");
        Assert.IsFalse(state.Detail.Contains("Repeating"));
    }

    [TestMethod]
    [DataRow(PlaybackPhase.Completed)]
    [DataRow(PlaybackPhase.Cancelled)]
    [DataRow(PlaybackPhase.Failed)]
    public void TerminalLoopStatesDoNotClaimTheyAreStillRepeating(PlaybackPhase phase)
    {
        var state = RunControllerPresentation.ForPlayback(new(phase, 0, 0,
            TimeSpan.Zero, TimeSpan.FromSeconds(9), RepeatUntilStopped: true), false);
        Assert.IsFalse(state.CanStop);
        Assert.IsFalse(state.Detail.Contains("Repeating"));
        Assert.AreEqual("No playback is running", state.Note);
    }

    [TestMethod]
    public void NativeFailureDetailsAndCapturedEventCountsRemainAvailable()
    {
        const string error = "Target rejected input (native error 5).";
        var failed = RunControllerPresentation.ForPlayback(new(PlaybackPhase.Failed, 2, 4,
            TimeSpan.Zero, TimeSpan.FromSeconds(2), error), false);
        Assert.AreEqual(error, failed.Note);
        Assert.IsFalse(failed.CanStop);
        var capture = RunControllerPresentation.ForRecording(true, false, false, TimeSpan.FromSeconds(2), 42);
        Assert.AreEqual("42 raw events captured", capture.Detail);
        StringAssert.Contains(capture.Note, "Ctrl + W");
        Assert.IsTrue(capture.CanStop);
        var saving = RunControllerPresentation.ForRecording(false, false, true, TimeSpan.FromSeconds(2), 42);
        Assert.AreEqual(capture.Clock, saving.Clock);
        Assert.AreEqual(capture.Detail, saving.Detail);
        Assert.IsFalse(saving.CanStop);
    }

    [TestMethod]
    public void SafetyAndValidationStayOutsideTheScrollingFieldsWithAnOverlayGutter()
    {
        var dialog = ReadXaml("Views", "ShellDialogContent.xaml");
        var scroller = dialog.Descendants(Xaml + "ScrollViewer").Single();
        Assert.AreEqual("Auto", (string?)scroller.Attribute("VerticalScrollBarVisibility"));
        Assert.AreEqual("Disabled", (string?)scroller.Attribute("HorizontalScrollBarVisibility"));
        var padding = ((string)scroller.Attribute("Padding")!).Split(',').Select(double.Parse).ToArray();
        Assert.IsTrue(padding[2] >= 20, "Overlay scrollbars must have their own gutter beside fields.");
        foreach (var name in new[] { "SafetyShortcut", "ValidationError" })
        {
            var pinned = dialog.Descendants().Single(e => (string?)e.Attribute(XamlNames + "Name") == name);
            Assert.IsFalse(pinned.Ancestors(Xaml + "ScrollViewer").Any(), $"{name} must stay visible while fields scroll.");
            var footer = pinned.Ancestors().Single(e => e.Attribute("Grid.Row") is not null);
            var rows = footer.Parent!.Element(Xaml + "Grid.RowDefinitions")!.Elements().ToArray();
            Assert.AreEqual("Auto", (string?)rows[int.Parse((string)footer.Attribute("Grid.Row")!)].Attribute("Height"));
            Assert.AreEqual("*", (string?)rows[int.Parse((string)scroller.Attribute("Grid.Row")!)].Attribute("Height"));
        }
    }

    [TestMethod]
    public void ActionTextRetainsContrastInLightAndDarkAcrossPointerStates()
    {
        var resources = ReadXaml("Themes", "DesignTheme.xaml");
        foreach (var theme in new[] { "Light", "Default" })
        {
            var palette = resources.Descendants(Xaml + "ResourceDictionary").Single(e => (string?)e.Attribute(XamlNames + "Key") == theme);
            double[] Color(string key) => Hex((string)palette.Elements().Single(e => (string?)e.Attribute(XamlNames + "Key") == key).Attribute("Color")!);
            var paper = Color("MacroPaperBrush");
            foreach (var (background, foreground) in new[] { ("MacroBlueBrush", "MacroBlueTextBrush"), ("MacroRedBrush", "MacroRedTextBrush") })
            {
                var fill = Color(background);
                var text = Color(foreground);
                var opacities = new[] { 1d }.Concat(palette.Elements().Where(e => new[] { "MacroButtonHoverOpacity", "MacroButtonPressedOpacity" }.Contains((string?)e.Attribute(XamlNames + "Key"))).Select(e => double.Parse(e.Value)));
                foreach (var opacity in opacities)
                {
                    var blended = fill.Zip(paper, (color, under) => color * opacity + under * (1 - opacity)).ToArray();
                    var luminances = new[] { Luminance(blended), Luminance(text) }.Order().ToArray();
                    Assert.IsTrue((luminances[1] + .05) / (luminances[0] + .05) >= 4.5,
                        $"{theme} {background} at {opacity}: 14px action text needs 4.5:1 contrast.");
                }
            }
        }
    }

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XamlNames = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static XDocument ReadXaml(string folder, string file, [CallerFilePath] string source = "")
        => XDocument.Load(Path.Combine(Path.GetDirectoryName(source)!, "..", "MacroRecorderGUI", folder, file));
    private static double[] Hex(string color) => Enumerable.Range(0, 3).Select(i => Convert.ToInt32(color.Substring(1 + i * 2, 2), 16) / 255d).ToArray();
    private static double Luminance(double[] rgb)
    {
        var linear = rgb.Select(c => c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4)).ToArray();
        return linear[0] * .2126 + linear[1] * .7152 + linear[2] * .0722;
    }
}
