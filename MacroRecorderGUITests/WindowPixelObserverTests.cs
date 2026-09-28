using MacroRecorderGUI.Models;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WindowPixelObserverTests
{
    private static readonly ObservedWindow Target = new(10, 20, 30, "Class", "Export", @"C:\app.exe", true, false);
    private sealed class Desktop : IWindowPixelDesktop
    {
        public WindowObservation Found = new([]);
        public PixelObservation Pixel = new(0x123456, "desktop");
        public WindowSelector? Selector;
        public CancellationToken Token;
        public ObservedWindow? PixelWindow;
        public PixelCondition? PixelCondition;
        public int Finds, Reads;
        public WindowObservation FindWindows(WindowSelector selector, CancellationToken token)
        { Finds++; Selector = selector; Token = token; return Found; }
        public PixelObservation ReadPixel(PixelCondition condition, ObservedWindow? window)
        { Reads++; PixelCondition = condition; PixelWindow = window; return Pixel; }
    }

    [TestMethod]
    [DataRow(WindowTest.Exists, 3)]
    [DataRow(WindowTest.Visible, 1)]
    [DataRow(WindowTest.Foreground, 1)]
    [DataRow(WindowTest.Absent, 0)]
    public async Task AnyWindowKeepsAllResolvedIdentitiesSeparateFromPredicateMatches(WindowTest test, int matches)
    {
        var windows = new[] { Target, Target with { Handle = 11, Visible = false, Foreground = true }, Target with { Handle = 12, Visible = false } };
        var desktop = new Desktop { Found = new(windows) };
        var condition = ConditionalWaitTests.Condition();
        condition.Window.Target.AnyMatch = true; condition.Window.Test = test;
        var result = await new WindowPixelObserver(desktop).ObserveAsync(condition, default);
        Assert.AreEqual(matches > 0 ? ObservationState.Match : ObservationState.NoMatch, result.State);
        Assert.AreEqual(matches > 0 ? 1u : 0u, result.Value);
        Assert.AreEqual("", result.Identity);
        CollectionAssert.AreEqual(windows.Select(window => window.Identity).ToArray(), result.Instances!.ToArray());
        var expected = test switch
        {
            WindowTest.Exists => windows.Select(window => window.Identity).ToArray(),
            WindowTest.Visible => new[] { windows[0].Identity },
            WindowTest.Foreground => new[] { windows[1].Identity },
            _ => Array.Empty<string>()
        };
        CollectionAssert.AreEqual(expected, result.MatchingInstances!.ToArray());
        Assert.AreEqual(0, desktop.Reads);
    }

    [TestMethod]
    public async Task SingleWindowIdentityChangesForHandlePidOrProcessCreationReuse()
    {
        var desktop = new Desktop();
        var observer = new WindowPixelObserver(desktop);
        var identities = new HashSet<string>();
        foreach (var window in new[] { Target, Target with { Handle = 11 }, Target with { ProcessId = 21 }, Target with { ProcessCreated = 31 } })
        {
            desktop.Found = new([window]);
            var result = await observer.ObserveAsync(ConditionalWaitTests.Condition(), default);
            Assert.AreEqual(window.Identity, result.Identity);
            Assert.IsTrue(identities.Add(result.Identity), "Reused handles or process IDs must not retain the old instance identity.");
        }
    }

    [TestMethod]
    [DataRow(0, (int)ObservationState.Unavailable)]
    [DataRow(2, (int)ObservationState.Error)]
    public async Task MissingOrAmbiguousClientTargetDoesNotFallBackToDesktop(int count, int expected)
    {
        var desktop = new Desktop { Found = new(Enumerable.Range(0, count).Select(i => Target with { Handle = 10 + i }).ToArray()) };
        var condition = ConditionalWaitTests.Condition();
        condition.Pixel = new() { Coordinates = PixelCoordinates.ClientPhysical, Target = condition.Window.Target, NotEqual = true };
        var result = await new WindowPixelObserver(desktop).ObserveAsync(condition, default);
        Assert.AreEqual((ObservationState)expected, result.State);
        Assert.AreEqual(1, desktop.Finds);
        Assert.AreEqual(0, desktop.Reads);
        Assert.AreEqual("", result.Identity);
    }

    [TestMethod]
    [DataRow((int)ObservationState.Unavailable)]
    [DataRow((int)ObservationState.Error)]
    public async Task EnumerationFailureCannotEstablishAbsenceEvenWithPartialMatches(int failure)
    {
        var desktop = new Desktop { Found = new([Target], (ObservationState)failure, "candidate access denied") };
        var condition = ConditionalWaitTests.Condition(); condition.Window.Test = WindowTest.Absent;
        var result = await new WindowPixelObserver(desktop).ObserveAsync(condition, default);
        Assert.AreEqual((ObservationState)failure, result.State);
        Assert.AreEqual("candidate access denied", result.Detail);
        Assert.IsNull(result.Instances, "An incomplete enumeration cannot become a new-window baseline.");
        Assert.AreEqual(0, desktop.Reads);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PixelResolutionForwardsOnlyItsSelectedTargetAndPreservesSample(bool client)
    {
        using var cancellation = new CancellationTokenSource();
        var selector = new WindowSelector { Title = "Export", WindowClass = "Class", ExecutablePath = @"C:\app.exe", TitleMatch = TitleMatch.Contains, IgnoreTitleCase = true };
        var condition = ConditionalWaitTests.Condition();
        condition.Pixel = new() { Target = client ? selector : null, Coordinates = client ? PixelCoordinates.ClientPhysical : PixelCoordinates.DesktopPhysical, Rgb = 0x123456 };
        var desktop = new Desktop { Found = new([Target]), Pixel = new(0x123456, client ? Target.Identity : "desktop") };
        var result = await new WindowPixelObserver(desktop).ObserveAsync(condition, cancellation.Token);
        Assert.AreEqual(ObservationState.Match, result.State);
        Assert.AreEqual(0x123456u, result.Value);
        Assert.AreEqual(desktop.Pixel.Identity, result.Identity);
        Assert.AreEqual(client ? 1 : 0, desktop.Finds);
        Assert.AreEqual(1, desktop.Reads);
        Assert.AreSame(client ? selector : null, desktop.Selector);
        Assert.AreSame(client ? Target : null, desktop.PixelWindow);
        Assert.AreSame(condition.Pixel, desktop.PixelCondition);
        if (client) Assert.AreEqual(cancellation.Token, desktop.Token);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelledObservationNeverEnumeratesOrSamples(bool pixel)
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var desktop = new Desktop();
        var condition = ConditionalWaitTests.Condition(); if (pixel) condition.Pixel = new();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await new WindowPixelObserver(desktop).ObserveAsync(condition, cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.AreEqual(0, desktop.Finds); Assert.AreEqual(0, desktop.Reads);
    }
}
