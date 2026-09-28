using Google.Protobuf;
using MacroRecorder.Waiting;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

public sealed partial class HiddenFocusTests
{
    private sealed class SourceQueriesFake : IWaitSourceQueries
    {
        public int Calls;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<WaitSourceChoices<WaitProcessChoice>>? PendingProcesses;
        public bool FailLanguages;
        public Task<WaitSourceChoices<AccessibilityChoice>> AccessibilityAsync(WindowSelector target, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new WaitSourceChoices<AccessibilityChoice>(ReadStatus.Success,
                [new("status element", new() { AutomationId = "chosen-status", ControlType = 50020 }, [new() { AutomationId = "panel" }])]));
        }
        public Task<WaitSourceChoices<WaitProcessChoice>> ProcessesAsync(CancellationToken token)
        {
            Calls++; Started.TrySetResult();
            return PendingProcesses?.Task ?? Task.FromResult(new WaitSourceChoices<WaitProcessChoice>(ReadStatus.Success,
                [new("Example app", @"C:\Apps\Chosen.exe", 10, 42)], "Some inaccessible processes were skipped.", false));
        }
        public Task<WaitSourceChoices<WaitModuleChoice>> ModulesAsync(WaitProcessChoice process, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new WaitSourceChoices<WaitModuleChoice>(ReadStatus.Success,
                [new("Example module", @"C:\Apps\Chosen.dll", "2.0.0.0")]));
        }
        public Task<WaitSourceChoices<string>> LanguagesAsync(CancellationToken token)
        {
            Calls++;
            return Task.FromResult(FailLanguages ? new WaitSourceChoices<string>(ReadStatus.Unavailable, [], "Install the requested language in the configured tessdata directory.")
                : new WaitSourceChoices<string>(ReadStatus.Success, ["eng", "fra"]));
        }
    }

    private static WaitConditionEditor SourceFields(WaitCondition condition, HiddenWaitObserver observer,
        SourceQueriesFake queries, WaitSourcePreferencesTests.MemoryStore? store = null)
    {
        var fields = new WaitConditionEditor(condition, new WaitRunner(observer), new FakeWaitPicker(), (_, _) => Task.CompletedTask);
        fields.UseSourceQueries(queries);
        fields.UseSourcePreferences(new WaitSourcePreferences(store ?? new(), new WaitLocalSettings()));
        return fields;
    }

    private static async Task CheckWaitSourceControls()
    {
        CheckSourceRoundTrips();
        CheckSourceDraftValidationAndSwitching();
        await CheckMemoryChangesDraftRepair();
        CheckSourceRegionSelection();
        await CheckSourceChoiceQueries();
        await CheckSourceQueryCancellation();
        await CheckSourceLocalPermission();
        await CheckSourceCommitUndoAndPreview();
    }

    private static void CheckSourceRoundTrips()
    {
        var newWindow = ConditionalWaitTests.Condition(WaitTrigger.NewWindow);
        using (var classic = SourceFields(newWindow, new(), new()))
        {
            CollectionAssert.AreEqual(newWindow.ToByteArray(), classic.Read().ToByteArray(), "Extending the source list must retain the window-only trigger on open.");
            Assert.IsFalse(Field<bool>(classic, "_additionalSourcesCreated"));
        }
        var accessibility = WaitSourceTextTests.Accessibility();
        accessibility.AccessibilityText.Ancestors.Add(new AccessibilitySelector { AutomationId = "parent" });
        var ocr = WaitSourceTextTests.Ocr(); var memory = WaitSourceTextTests.Memory();
        IMessage[] nested = [accessibility, accessibility.AccessibilityText, accessibility.AccessibilityText.Target,
            accessibility.AccessibilityText.Element, accessibility.AccessibilityText.Ancestors[0], accessibility.AccessibilityText.Predicate,
            ocr, ocr.OcrText, ocr.OcrText.Region, ocr.OcrText.Predicate, memory, memory.Memory, memory.Memory.Module];
        foreach (var message in nested) message.MergeFrom(new byte[] { 0xa0, 0x06, 0x29 });
        foreach (var condition in new[] { accessibility, ocr, memory })
        {
            var observer = new HiddenWaitObserver(); var queries = new SourceQueriesFake(); var store = new WaitSourcePreferencesTests.MemoryStore();
            using var fields = SourceFields(condition, observer, queries, store);
            var window = new Window { Content = fields };
            try
            {
                LayoutControl(fields, 420, 800);
                var before = condition.ToByteArray();
                CollectionAssert.AreEqual(before, fields.Read().ToByteArray(), "Every source and nested unknown field must round-trip untouched.");
                Assert.IsFalse(fields.IsDirty); Assert.AreEqual(3, fields.Trigger.Items.Count);
                Assert.AreEqual(0, observer.Calls); Assert.AreEqual(0, queries.Calls); Assert.AreEqual(0, store.Loads); Assert.AreEqual(0, store.Saves);
                Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
                fields.Timeout.Text = "31";
                var expected = condition.Clone(); expected.TimeoutUs = 31_000_000;
                CollectionAssert.AreEqual(expected.ToByteArray(), fields.Read().ToByteArray());
                CollectionAssert.AreEqual(before, condition.ToByteArray(), "Editing a draft cannot mutate the caller's condition.");
            }
            finally { window.Close(); }
        }
    }

    private static void CheckSourceDraftValidationAndSwitching()
    {
        var observer = new HiddenWaitObserver(); var queries = new SourceQueriesFake();
        using var classic = SourceFields(ConditionalWaitTests.Condition(), observer, queries);
        Assert.IsFalse(Field<bool>(classic, "_additionalSourcesCreated"));
        classic.Timeout.Text = ""; classic.Source.SelectedIndex = 2;
        Assert.IsTrue(Field<bool>(classic, "_additionalSourcesCreated")); Assert.AreEqual("", classic.Timeout.Text);
        Assert.IsNotNull(classic.ElementId, "An invalid timing draft must not prevent the newly selected source form from appearing.");
        using var fields = SourceFields(WaitSourceTextTests.Memory(), observer, queries);
        fields.MemoryOffsets.Text = "0x20,";
        StringAssert.Contains(Assert.ThrowsExactly<ArgumentException>(() => fields.Read()).Message, "Pointer offset 2");
        fields.Source.SelectedIndex = 2; fields.Source.SelectedIndex = 4;
        Assert.AreEqual("0x20,", fields.MemoryOffsets.Text, "Switching source keeps incomplete drafts.");
        fields.MemoryOffsets.Text = ""; Assert.AreEqual(0, fields.Read().Memory.PointerOffsets.Count);
        fields.MemoryAddress.Text = "0xFFFFFFFFFFFFFFFF";
        Assert.AreEqual(ulong.MaxValue, fields.Read().Memory.Module.Offset);
        fields.MemoryType.SelectedIndex = (int)MemoryScalarType.Uint16;
        fields.MemoryExpected.Text = "65536";
        Assert.ThrowsExactly<ArgumentException>(() => fields.Read());
        fields.MemoryExpected.Text = "65535";
        Assert.AreEqual("65535", fields.Read().Memory.Expected);
        fields.MemoryTolerance.Text = ""; fields.Stability.Text = "";
        var read = fields.Read(); Assert.AreEqual(0d, read.Memory.Tolerance); Assert.AreEqual(0UL, read.StableForUs);
        Assert.AreEqual("", fields.Stability.Text, "Blank normalization waits for commit.");
        fields.Committed(); Assert.AreEqual("0", fields.Stability.Text); Assert.AreEqual("0", fields.MemoryTolerance.Text);
        fields.Timeout.Text = ""; Assert.Throws<Exception>(() => fields.Read());
        fields.ApplyWindowCapture(new() { Title = "New target" }); Assert.AreEqual("", fields.Timeout.Text);
        fields.Timeout.Text = "30"; fields.Poll.Text = "";
        fields.Source.SelectedIndex = 3; fields.Source.SelectedIndex = 4;
        Assert.AreEqual("", fields.Poll.Text); Assert.Throws<Exception>(() => fields.Read());
        foreach (var invalid in new[] { "0", "-1", "99", "100.0001" })
        {
            fields.Poll.Text = invalid; fields.Source.SelectedIndex = 3; fields.Source.SelectedIndex = 4;
            Assert.AreEqual(invalid, fields.Poll.Text, "Source changes must not repair invalid numeric polling drafts.");
            Assert.Throws<Exception>(() => fields.Read());
        }
        fields.Poll.Text = "100"; fields.MemoryTolerance.Text = "NaN";
        StringAssert.Contains(Assert.ThrowsExactly<ArgumentException>(() => fields.Read()).Message, "tolerance");
        Assert.AreEqual(0, observer.Calls); Assert.AreEqual(0, queries.Calls);
    }

    private static async Task CheckMemoryChangesDraftRepair()
    {
        var condition = WaitSourceTextTests.Memory();
        condition.Memory.MergeFrom(new byte[] { 0xa0, 0x06, 0x29 });
        var before = condition.ToByteArray();
        var observer = new HiddenWaitObserver(); var queries = new SourceQueriesFake();
        using var fields = SourceFields(condition, observer, queries);
        fields.MemoryOptIn.IsChecked = true; await fields.SaveMemoryPolicyAsync();
        foreach (var (type, invalid) in new[] { (MemoryScalarType.Uint64, "unfinished"), (MemoryScalarType.Uint8, "256"), (MemoryScalarType.Float64, "NaN") })
        {
            fields.Trigger.SelectedIndex = (int)WaitTrigger.IsTrue;
            fields.MemoryType.SelectedIndex = (int)type; fields.MemoryExpected.Text = invalid;
            fields.Trigger.SelectedIndex = (int)WaitTrigger.Changes;
            Assert.AreEqual(Visibility.Visible, fields.MemoryExpected.Visibility);
            Assert.AreEqual(Visibility.Visible, fields.MemoryComparison.Visibility);
            Assert.IsTrue(fields.MemoryExpected.IsEnabled);
            StringAssert.Contains(fields.MemoryExpected.Header.ToString()!, "not used for Changes");
            StringAssert.Contains(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(fields.MemoryExpected), "not used for Changes");
            Assert.AreEqual(Visibility.Visible, Field<TextBlock>(fields, "_memoryComparisonNote").Visibility);
            Assert.ThrowsExactly<ArgumentException>(() => fields.Read());
            await fields.TestAsync();
            Assert.AreEqual(invalid, fields.MemoryExpected.Text, "Validation cannot replace the saved draft.");
            Assert.AreEqual(0, observer.Calls, "An invalid saved predicate must fail before any observation.");
            fields.Source.SelectedIndex = 3; fields.Source.SelectedIndex = 4;
            Assert.AreEqual(invalid, fields.MemoryExpected.Text);
            Assert.AreEqual(Visibility.Visible, fields.MemoryExpected.Visibility);
            fields.MemoryExpected.Text = "7";
            var repaired = fields.Read();
            Assert.AreEqual(WaitTrigger.Changes, repaired.Trigger); Assert.AreEqual("7", repaired.Memory.Expected);
            Assert.AreEqual(Visibility.Visible, fields.MemoryExpected.Visibility, "The repair field must not disappear while typing a valid value.");
        }
        fields.MemoryType.SelectedIndex = (int)condition.Memory.ScalarType;
        fields.Trigger.SelectedIndex = (int)WaitTrigger.IsTrue;
        fields.Poll.Text = "100";
        var expected = condition.Clone(); expected.Memory.Expected = "7";
        CollectionAssert.AreEqual(expected.ToByteArray(), fields.Read().ToByteArray());
        CollectionAssert.AreEqual(before, condition.ToByteArray());
        Assert.AreEqual("Expected number (decimal)", fields.MemoryExpected.Header);
        Assert.AreEqual(Visibility.Collapsed, Field<TextBlock>(fields, "_memoryComparisonNote").Visibility);
        Assert.AreEqual(0, queries.Calls);
    }

    private static void CheckSourceRegionSelection()
    {
        using var fields = SourceFields(WaitSourceTextTests.Ocr(), new(), new());
        fields.Timeout.Text = "unfinished";
        fields.ApplyFirstRegionCorner(new(Pixel: new(-30, -20, 0)));
        fields.ApplyRegionCorner(new(Pixel: new(-10, -5, 0)));
        Assert.AreEqual("-30", fields.RegionX.Text); Assert.AreEqual("-20", fields.RegionY.Text);
        Assert.AreEqual("21", fields.RegionWidth.Text); Assert.AreEqual("16", fields.RegionHeight.Text);
        Assert.AreEqual("unfinished", fields.Timeout.Text);
        fields.ApplyFirstRegionCorner(new(Pixel: new(int.MinValue, int.MinValue, 0)));
        fields.ApplyRegionCorner(new(Pixel: new(int.MaxValue, int.MaxValue, 0)));
        Assert.AreEqual("21", fields.RegionWidth.Text, "An oversized selection leaves the previous draft intact.");
        fields.Timeout.Text = "30"; fields.RegionWidth.Text = "0";
        Assert.ThrowsExactly<ArgumentException>(() => fields.Read());
        fields.RegionWidth.Text = "21"; fields.RegionCoordinates.SelectedIndex = (int)PixelCoordinates.ClientPhysical;
        fields.Title.Text = "Export";
        Assert.ThrowsExactly<ArgumentException>(() => fields.Read(), "Negative desktop coordinates cannot become client offsets silently.");
    }

    private static async Task CheckSourceChoiceQueries()
    {
        var observer = new HiddenWaitObserver(); var queries = new SourceQueriesFake();
        using var fields = SourceFields(WaitSourceTextTests.Memory(), observer, queries);
        var original = fields.Read().ToByteArray();
        await fields.LoadProcessesAsync(); Assert.AreEqual(0, queries.Calls, "Local opt-in is required even for explicit process listing.");
        fields.MemoryOptIn.IsChecked = true; await fields.SaveMemoryPolicyAsync();
        await fields.LoadProcessesAsync();
        Assert.AreEqual(1, queries.Calls); Assert.AreEqual(-1, fields.ProcessChoices.SelectedIndex);
        StringAssert.Contains(fields.Feedback.Text, "inaccessible processes were skipped");
        CollectionAssert.AreEqual(original, fields.Read().ToByteArray(), "Loading a list never chooses a target.");
        fields.MemoryOffsets.Text = "unfinished";
        fields.ProcessChoices.SelectedIndex = 0;
        Assert.AreEqual(@"C:\Apps\Chosen.exe", fields.MemoryExecutable.Text); Assert.AreEqual("unfinished", fields.MemoryOffsets.Text);
        Assert.AreEqual(Visibility.Visible, fields.ProcessChoiceWarning.Visibility);
        StringAssert.Contains(fields.ProcessChoiceWarning.Text, "inaccessible processes were skipped");
        await fields.LoadModulesAsync(); fields.ModuleChoices.SelectedIndex = 0;
        Assert.AreEqual(@"C:\Apps\Chosen.dll", fields.MemoryModulePath.Text); Assert.AreEqual("2.0.0.0", fields.MemoryModuleVersion.Text);
        Assert.AreEqual("unfinished", fields.MemoryOffsets.Text);
        fields.MemoryExecutable.Text = @"C:\Other.exe";
        Assert.AreEqual(Visibility.Collapsed, fields.ModuleChoices.Visibility);
        fields.Source.SelectedIndex = 2; fields.Title.Text = "Export";
        await fields.LoadAccessibilityChoicesAsync(); fields.AccessibilityChoices.SelectedIndex = 0;
        Assert.AreEqual("chosen-status", fields.ElementId.Text);
        Assert.AreEqual("panel", fields.Read().AccessibilityText.Ancestors.Single().AutomationId);
        fields.Source.SelectedIndex = 3; fields.OcrLanguage.Text = "missing"; queries.FailLanguages = true;
        await fields.LoadLanguagesAsync();
        Assert.AreEqual("missing", fields.OcrLanguage.Text); StringAssert.Contains(fields.Feedback.Text, "Install the requested language");
        queries.FailLanguages = false; await fields.LoadLanguagesAsync();
        Assert.AreEqual("missing", fields.OcrLanguage.Text); fields.LanguageChoices.SelectedIndex = 1; Assert.AreEqual("fra", fields.OcrLanguage.Text);
        Assert.AreEqual(0, observer.Calls);
    }

    private static async Task CheckSourceQueryCancellation()
    {
        var queries = new SourceQueriesFake { PendingProcesses = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var fields = SourceFields(WaitSourceTextTests.Memory(), new(), queries);
        fields.MemoryOptIn.IsChecked = true; await fields.SaveMemoryPolicyAsync();
        var task = fields.LoadProcessesAsync(); await queries.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        fields.MemoryExpected.Text = "12";
        await task.WaitAsync(TimeSpan.FromSeconds(2));
        var expectedFeedback = fields.Feedback.Text;
        queries.PendingProcesses.SetResult(new(ReadStatus.Success, [new("Late", @"C:\Late.exe")]));
        await Task.Yield();
        Assert.AreEqual(expectedFeedback, fields.Feedback.Text); Assert.AreEqual("12", fields.MemoryExpected.Text);
        Assert.AreEqual(Visibility.Collapsed, fields.ProcessChoices.Visibility);
        Assert.AreEqual(@"C:\Apps\Example.exe", fields.MemoryExecutable.Text);
    }

    private static async Task CheckSourceLocalPermission()
    {
        var store = new WaitSourcePreferencesTests.MemoryStore(); var local = new WaitLocalSettings();
        var preferences = new WaitSourcePreferences(store, local);
        using var fields = SourceFields(WaitSourceTextTests.Memory(), new(), new(), store);
        fields.UseSourcePreferences(preferences);
        var before = fields.Read().ToByteArray();
        Assert.IsFalse(fields.MemoryOptIn.IsChecked == true); Assert.IsFalse(local.MemoryEnabled); Assert.AreEqual(0, store.Loads);
        await fields.TestAsync(); StringAssert.Contains(fields.Feedback.Text, "Enable read-only memory");
        fields.MemoryOptIn.IsChecked = true; await fields.SaveMemoryPolicyAsync();
        Assert.IsTrue(local.MemoryEnabled); Assert.AreEqual(1, store.Saves);
        CollectionAssert.AreEqual(before, fields.Read().ToByteArray()); Assert.IsFalse(fields.IsDirty);
        fields.MemoryOptIn.IsChecked = false; await fields.SaveMemoryPolicyAsync(); Assert.IsFalse(local.MemoryEnabled);
        store.FailSave = true; fields.MemoryOptIn.IsChecked = true; await fields.SaveMemoryPolicyAsync();
        Assert.IsFalse(local.MemoryEnabled); Assert.IsFalse(fields.MemoryOptIn.IsChecked == true);
        using var reopened = SourceFields(WaitSourceTextTests.Memory(), new(), new(), store);
        Assert.IsFalse(reopened.MemoryOptIn.IsChecked == true, "Importing a memory condition cannot grant a local permission.");
    }

    private static async Task CheckSourceCommitUndoAndPreview()
    {
        foreach (var condition in new[] { WaitSourceTextTests.Accessibility(), WaitSourceTextTests.Ocr(), WaitSourceTextTests.Memory() })
        {
            using var macro = new MacroViewModel("Source form", new FakePlaybackEngine());
            macro.AddEvent(new WaitConditionEvent(condition));
            using var editor = new MacroTabContent { DataContext = macro };
            var window = new Window { Content = editor };
            try
            {
                Call(editor, "Attach"); Field<ListView>(editor, "ActionsList").SelectedIndex = 0;
                var observer = new HiddenWaitObserver(); var queries = new SourceQueriesFake();
                using var fields = SourceFields(condition, observer, queries);
                Field<WaitConditionEditor>(editor, "_conditionEditor").Dispose();
                typeof(MacroTabContent).GetField("_conditionEditor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(editor, fields);
                Field<ContentControl>(editor, "WaitConditionHost").Content = fields;
                if (condition.Memory is not null)
                {
                    fields.MemoryExpected.Text = "unfinished"; fields.Trigger.SelectedIndex = (int)WaitTrigger.Changes;
                    Assert.IsFalse(editor.TryCommitPendingEdits());
                    Assert.AreEqual(Visibility.Visible, fields.MemoryExpected.Visibility);
                    Assert.AreEqual("unfinished", fields.MemoryExpected.Text);
                    fields.MemoryExpected.Text = "7";
                }
                fields.Timeout.Text = "invalid";
                Assert.IsFalse(editor.TryCommitPendingEdits()); Assert.AreEqual("invalid", fields.Timeout.Text);
                fields.Timeout.Text = "31"; Assert.IsTrue(editor.TryCommitPendingEdits());
                Assert.AreEqual(31_000_000UL, ((WaitConditionEvent)macro.Events[0]).Condition.TimeoutUs);
                if (condition.Memory is not null)
                {
                    Assert.AreEqual(WaitTrigger.Changes, ((WaitConditionEvent)macro.Events[0]).Condition.Trigger);
                    Assert.AreEqual("7", ((WaitConditionEvent)macro.Events[0]).Condition.Memory.Expected);
                }
                if (condition.Memory is not null) { fields.MemoryOptIn.IsChecked = true; await fields.SaveMemoryPolicyAsync(); }
                var run = fields.TestAsync(); await observer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
                fields.CancelTest(); await run.WaitAsync(TimeSpan.FromSeconds(2));
                observer.Response.TrySetResult(new(ObservationState.Match, "fake completion"));
                Assert.IsTrue(editor.Undo());
                CollectionAssert.AreEqual(condition.ToByteArray(), ((WaitConditionEvent)macro.Events[0]).Condition.ToByteArray());
                using var previewFields = SourceFields(condition, observer, queries);
                Field<WaitConditionEditor>(editor, "_conditionEditor").Dispose();
                typeof(MacroTabContent).GetField("_conditionEditor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(editor, previewFields);
                Field<ContentControl>(editor, "WaitConditionHost").Content = previewFields;
                editor.IsPreviewMode = true; editor.StepPreview(); editor.IsPreviewMode = false;
                Assert.AreEqual(1, observer.Calls); Assert.AreEqual(0, queries.Calls);
                Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
            }
            finally { window.Close(); }
        }
    }
}
