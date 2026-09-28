using Google.Protobuf;
using MacroRecorderGUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

public sealed partial class HiddenFocusTests
{
    private static async Task CheckTextChangesDraftRepair()
    {
        foreach (var condition in new[] { WaitSourceTextTests.Accessibility(), WaitSourceTextTests.Ocr() })
        {
            var originalPredicate = condition.AccessibilityText?.Predicate ?? condition.OcrText.Predicate;
            originalPredicate.Comparison = (TextComparison)99;
            originalPredicate.Whitespace = (TextWhitespace)99;
            originalPredicate.MergeFrom(new byte[] { 0xa0, 0x06, 0x29 });
            var before = condition.ToByteArray();
            var observer = new HiddenWaitObserver(); var queries = new SourceQueriesFake();
            using var fields = SourceFields(condition, observer, queries);
            var source = fields.Source.SelectedIndex;
            var predicate = condition.AccessibilityText is not null ? fields.AccessibilityPredicate : fields.OcrPredicate;
            Assert.IsFalse(fields.IsDirty);
            Assert.AreEqual(-1, predicate.Comparison.SelectedIndex);
            fields.Trigger.SelectedIndex = (int)WaitTrigger.Changes;
            Assert.AreEqual(Visibility.Visible, predicate.Comparison.Visibility);
            Assert.AreEqual(Visibility.Visible, predicate.Whitespace.Visibility);
            StringAssert.Contains(predicate.Comparison.Header.ToString()!, "not used for Changes");
            StringAssert.Contains(AutomationProperties.GetName(predicate.Comparison), "not used for Changes");
            Assert.ThrowsExactly<ArgumentException>(() => fields.Read());
            await fields.TestAsync(); Assert.AreEqual(0, observer.Calls);
            predicate.Comparison.SelectedIndex = (int)TextComparison.TextEquals;
            Assert.ThrowsExactly<ArgumentException>(() => fields.Read());
            predicate.Whitespace.SelectedIndex = (int)TextWhitespace.PreserveWhitespace;
            var expected = condition.Clone(); expected.Trigger = WaitTrigger.Changes;
            var expectedPredicate = expected.AccessibilityText?.Predicate ?? expected.OcrText.Predicate;
            expectedPredicate.Comparison = TextComparison.TextEquals; expectedPredicate.Whitespace = TextWhitespace.PreserveWhitespace;
            CollectionAssert.AreEqual(expected.ToByteArray(), fields.Read().ToByteArray());

            fields.Trigger.SelectedIndex = (int)WaitTrigger.IsTrue;
            predicate.Expected.Text = new string('x', 32769);
            fields.Trigger.SelectedIndex = (int)WaitTrigger.Changes;
            Assert.AreEqual(Visibility.Visible, predicate.Expected.Visibility);
            Assert.IsTrue(predicate.Expected.IsEnabled);
            StringAssert.Contains(AutomationProperties.GetName(predicate.Expected), "not used for Changes");
            Assert.AreEqual(Visibility.Visible, Field<TextBlock>(predicate, "_savedPredicateNote").Visibility);
            Assert.ThrowsExactly<ArgumentException>(() => fields.Read());
            await fields.TestAsync(); Assert.AreEqual(0, observer.Calls);
            fields.Source.SelectedIndex = 4; fields.Source.SelectedIndex = source;
            Assert.AreEqual(32769, predicate.Expected.Text.Length, "Source/trigger changes must retain oversized drafts.");
            Assert.AreEqual(Visibility.Visible, predicate.Expected.Visibility);
            predicate.Expected.Text = "Repaired text";
            expectedPredicate.Expected = "Repaired text";
            CollectionAssert.AreEqual(expected.ToByteArray(), fields.Read().ToByteArray());
            Assert.AreEqual(Visibility.Visible, predicate.Expected.Visibility, "The field stays available while repairing text.");
            fields.Trigger.SelectedIndex = (int)WaitTrigger.IsTrue;
            Assert.AreEqual("Expected text", predicate.Expected.Header);
            Assert.AreEqual("Text comparison", predicate.Comparison.Header);
            Assert.AreEqual(Visibility.Collapsed, Field<TextBlock>(predicate, "_savedPredicateNote").Visibility);
            CollectionAssert.AreEqual(before, condition.ToByteArray());
            Assert.AreEqual(0, queries.Calls);
        }
    }

    private static async Task CheckPixelChangesDraftRepair()
    {
        var condition = ConditionalWaitTests.Condition(WaitTrigger.Changes);
        condition.Pixel = new() { Coordinates = PixelCoordinates.DesktopPhysical, Rgb = 0x1000000 };
        condition.Pixel.MergeFrom(new byte[] { 0xa0, 0x06, 0x29 });
        var before = condition.ToByteArray();
        var observer = new HiddenWaitObserver(); var queries = new SourceQueriesFake();
        using var fields = SourceFields(condition, observer, queries);
        Assert.AreEqual(Visibility.Visible, fields.Rgb.Visibility);
        Assert.AreEqual(Visibility.Visible, fields.NotEqual.Visibility);
        StringAssert.Contains(AutomationProperties.GetName(fields.Rgb), "not used for Changes");
        Assert.ThrowsExactly<ArgumentException>(() => fields.Read());
        await fields.TestAsync(); Assert.AreEqual(0, observer.Calls);
        fields.Rgb.Text = "unfinished";
        fields.Source.SelectedIndex = 3; fields.Source.SelectedIndex = 1;
        Assert.AreEqual("unfinished", fields.Rgb.Text);
        Assert.AreEqual(Visibility.Visible, fields.Rgb.Visibility);
        Assert.ThrowsExactly<ArgumentException>(() => fields.Read());
        fields.Rgb.Text = "112233"; fields.NotEqual.IsChecked = true; fields.Poll.Text = "100";
        var expected = condition.Clone(); expected.Pixel.Rgb = 0x112233; expected.Pixel.NotEqual = true;
        CollectionAssert.AreEqual(expected.ToByteArray(), fields.Read().ToByteArray());
        CollectionAssert.AreEqual(before, condition.ToByteArray());
        Assert.AreEqual(0, queries.Calls);
    }

    private static void CheckSingleWindowDraftRepair()
    {
        var pixel = ConditionalWaitTests.Condition();
        pixel.Pixel = new() { Coordinates = PixelCoordinates.ClientPhysical, Target = new() { Title = "Export", AnyMatch = true } };
        var accessibility = WaitSourceTextTests.Accessibility(); accessibility.AccessibilityText.Target.AnyMatch = true;
        var ocr = WaitSourceTextTests.Ocr();
        ocr.OcrText.Region.X = 0; ocr.OcrText.Region.Coordinates = PixelCoordinates.ClientPhysical;
        ocr.OcrText.Region.Target = new() { Title = "Export", AnyMatch = true };
        foreach (var condition in new[] { pixel, accessibility, ocr })
        {
            var target = condition.Pixel?.Target ?? condition.AccessibilityText?.Target ?? condition.OcrText.Region.Target;
            target.MergeFrom(new byte[] { 0xa0, 0x06, 0x29 });
            var before = condition.ToByteArray();
            using var fields = SourceFields(condition, new(), new());
            Assert.IsFalse(fields.IsDirty);
            Assert.IsTrue(fields.Any.IsChecked == true);
            Assert.AreEqual(Visibility.Visible, fields.Any.Visibility);
            StringAssert.Contains(AutomationProperties.GetName(fields.Any), "requires one window");
            Assert.ThrowsExactly<ArgumentException>(() => fields.Read());
            fields.Trigger.SelectedIndex = (int)WaitTrigger.BecomesTrue;
            Assert.IsTrue(fields.Any.IsChecked == true, "Changing another field cannot silently repair an imported selector.");
            fields.Any.IsChecked = false;
            var expected = condition.Clone(); expected.Trigger = WaitTrigger.BecomesTrue;
            (expected.Pixel?.Target ?? expected.AccessibilityText?.Target ?? expected.OcrText.Region.Target).AnyMatch = false;
            CollectionAssert.AreEqual(expected.ToByteArray(), fields.Read().ToByteArray());
            CollectionAssert.AreEqual(before, condition.ToByteArray());
        }
    }
}
