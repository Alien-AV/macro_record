using MacroRecorder.Waiting;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class TextOcrWaitBackendTests
{
    private sealed class Desktop : IWindowPixelDesktop
    {
        public WindowObservation Result { get; set; } = new([new(11, 12, 13, "Fake", "Fake", "C:\\fake.exe", true, false)]);
        public WindowObservation FindWindows(WindowSelector selector, CancellationToken token) => Result;
        public PixelObservation ReadPixel(PixelCondition condition, ObservedWindow? window) => throw new AssertFailedException("Text must not read a pixel.");
    }
    private sealed class TextBackend : IAccessibilityTextBackend
    {
        public TextReadResult Result { get; set; } = new(ReadStatus.Success, "Ready", "element:1");
        public ValueTask<TextReadResult> ReadAsync(WaitWindow window, AccessibilityTextCondition condition, CancellationToken token) => ValueTask.FromResult(Result);
        public ValueTask<AccessibilityChoicesResult> GetChoicesAsync(WaitWindow window, CancellationToken token) => throw new AssertFailedException("Observation must not enumerate choices.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeCapture : IOcrCapture
    {
        public OcrCaptureResult Result { get; set; } = new(ReadStatus.Success, new(1, 1, [255, 255, 255, 255], "desktop"));
        public OcrCaptureResult Capture(OcrRegion region, ObservedWindow? window, CancellationToken token) => Result;
    }
    private sealed class Ocr : IOcrBackend
    {
        public int Calls;
        public TextReadResult Result { get; set; } = new(ReadStatus.Success, "Ready", "desktop");
        public ValueTask<TextReadResult> ReadAsync(OcrFrame frame, string language, CancellationToken token) { Calls++; return ValueTask.FromResult(Result); }
    }
    private sealed class Command : IOcrCommand
    {
        public int Calls;
        public OcrCommandResult Result { get; set; } = new(0, "Ready\n", "");
        public Task<OcrCommandResult> RunAsync(string executable, string tessdata, string language, byte[] bitmap, CancellationToken token)
        {
            Calls++; Assert.IsTrue(Path.IsPathFullyQualified(executable)); Assert.IsTrue(Path.IsPathFullyQualified(tessdata));
            Assert.AreEqual("eng", language); Assert.AreEqual((byte)'B', bitmap[0]); Assert.AreEqual(58, bitmap.Length);
            return Task.FromResult(Result);
        }
    }
    private static WaitCondition TextCondition()
    {
        var wait = WaitValidation.NewAccessibilityText();
        wait.AccessibilityText.Target.Title = "Fake"; wait.AccessibilityText.Element.AutomationId = "status";
        wait.AccessibilityText.Predicate.Expected = "Ready"; wait.StableForUs = 0;
        return wait;
    }

    [TestMethod]
    [DataRow(TextComparison.TextNotEquals)] [DataRow(TextComparison.TextNotContains)]
    public async Task MissingOrFailedAccessibilityCannotSatisfyNegativeText(TextComparison comparison)
    {
        var desktop = new Desktop(); var backend = new TextBackend();
        await using var observer = new AccessibilityTextObserver(desktop, backend);
        var condition = TextCondition(); condition.AccessibilityText.Predicate.Comparison = comparison;
        foreach (var status in new[] { ReadStatus.Unavailable, ReadStatus.Error })
        {
            backend.Result = new(status, "something different", "id", "failed");
            Assert.AreNotEqual(ObservationState.Match, (await observer.ObserveAsync(condition, default)).State);
        }
        desktop.Result = new([]);
        Assert.AreEqual(ObservationState.Unavailable, (await observer.ObserveAsync(condition, default)).State);
    }

    [TestMethod]
    public async Task TextComparisonUsesExplicitWhitespaceCaseAndFreshElementBaseline()
    {
        var backend = new TextBackend { Result = new(ReadStatus.Success, "  READY \r\n now\t", "id:1") };
        await using var observer = new AccessibilityTextObserver(new Desktop(), backend);
        var condition = TextCondition(); var predicate = condition.AccessibilityText.Predicate;
        predicate.Expected = "ready now"; predicate.IgnoreCase = true; predicate.Whitespace = TextWhitespace.CollapseWhitespace;
        var sample = await observer.ObserveAsync(condition, default); Assert.AreEqual(ObservationState.Match, sample.State);
        condition.Trigger = WaitTrigger.Changes; var evaluator = new WaitEvaluator(condition);
        Assert.IsFalse(evaluator.Sample(sample, TimeSpan.Zero));
        Assert.IsFalse(evaluator.Sample(sample with { Identity = "id:2", Text = "changed" }, TimeSpan.FromMilliseconds(250)));
        Assert.IsTrue(evaluator.Sample(sample with { Text = "changed" }, TimeSpan.FromMilliseconds(500)));
    }

    [TestMethod]
    public async Task ReplacedAccessibilityWindowCannotSupplyAnotherTargetsText()
    {
        var desktop = new Desktop(); await using var observer = new AccessibilityTextObserver(desktop, new TextBackend());
        Assert.AreEqual(ObservationState.Match, (await observer.ObserveAsync(TextCondition(), default)).State);
        desktop.Result = new([desktop.Result.Windows[0] with { ProcessCreated = 999 }]);
        Assert.AreEqual(ObservationState.Error, (await observer.ObserveAsync(TextCondition(), default)).State);
    }

    [TestMethod]
    [DataRow(ReadStatus.Unavailable)] [DataRow(ReadStatus.Error)]
    public async Task CaptureFailureNeverInvokesOcrOrSatisfiesNegative(ReadStatus status)
    {
        var capture = new FakeCapture { Result = new(status, Detail: "covered") }; var ocr = new Ocr();
        await using var observer = new OcrTextObserver(new Desktop(), capture, ocr);
        var condition = WaitValidation.NewOcrText(); condition.OcrText.Predicate.Comparison = TextComparison.TextNotContains;
        Assert.AreNotEqual(ObservationState.Match, (await observer.ObserveAsync(condition, default)).State);
        Assert.AreEqual(0, ocr.Calls);
    }

    [TestMethod]
    public async Task FailedOrOverlongRecognitionCannotMatchNegativeText()
    {
        var ocr = new Ocr(); await using var observer = new OcrTextObserver(new Desktop(), new FakeCapture(), ocr);
        var condition = WaitValidation.NewOcrText(); condition.OcrText.Predicate.Comparison = TextComparison.TextNotEquals;
        foreach (var result in new[] { new TextReadResult(ReadStatus.Error, "different", "desktop"), new(ReadStatus.Success, new string('a', 32769), "desktop") })
        {
            ocr.Result = result; Assert.AreNotEqual(ObservationState.Match, (await observer.ObserveAsync(condition, default)).State);
        }
    }

    [TestMethod]
    public async Task LocalOcrRequiresInstalledLanguageAndUsesOnlyBoundedInMemoryImage()
    {
        var directory = Directory.CreateTempSubdirectory("macro-local-ocr-tests-");
        try
        {
            var executable = Path.Combine(directory.FullName, "tesseract.exe");
            File.WriteAllText(executable, "fake; must never execute");
            var options = new WaitLocalOptions(false, executable, directory.FullName);
            var command = new Command(); var backend = new TesseractOcrBackend(options, command);
            var frame = new OcrFrame(1, 1, [0, 0, 0, 255], "desktop");
            Assert.AreEqual(ReadStatus.Unavailable, (await backend.ReadAsync(frame, "eng", default)).Status);
            Assert.AreEqual(0, command.Calls);
            File.WriteAllBytes(Path.Combine(directory.FullName, "eng.traineddata"), [1]);
            var languages = TesseractOcrBackend.GetLanguages(options, default);
            Assert.AreEqual(ReadStatus.Success, languages.Status); CollectionAssert.AreEqual(new[] { "eng" }, languages.Choices.ToArray());
            var text = await backend.ReadAsync(frame, "eng", default);
            Assert.AreEqual(ReadStatus.Success, text.Status); Assert.AreEqual("Ready", text.Text); Assert.AreEqual(1, command.Calls);
            command.Result = new(1, "wrong", "model failed");
            Assert.AreEqual(ReadStatus.Error, (await backend.ReadAsync(frame, "eng", default)).Status);
            Assert.Throws<ArgumentException>(() => TesseractOcrBackend.EncodeBitmap(new(4096, 4096, [], "desktop")));
        }
        finally { directory.Delete(true); }
    }

    [TestMethod]
    public void TextBoundsAndEmptySuccessfulReadsRemainDistinctFromFailure()
    {
        var predicate = new TextPredicate { Expected = "Ready", Comparison = TextComparison.TextNotContains };
        Assert.AreEqual(ObservationState.Match, TextPredicates.Observe(new(ReadStatus.Success, "", "id"), predicate).State);
        Assert.AreEqual(ObservationState.Unavailable, TextPredicates.Observe(new(ReadStatus.Unavailable), predicate).State);
        Assert.AreEqual(ObservationState.Unavailable, TextPredicates.Observe(new(ReadStatus.Success, new string('a', 32769), "id"), predicate).State);
    }
}
