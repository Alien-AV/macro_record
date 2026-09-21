using System.Reflection;
using MacroRecorderGUI;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace MacroRecorderGUITests;

public sealed partial class HiddenFocusTests
{
    private static async Task CheckLibraryControls()
    {
        var temporary = Directory.CreateTempSubdirectory("macro-hidden-library-");
        Window? window = null;
        try
        {
            using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), new RecordingLibraryStore(temporary.FullName));
            var one = await vm.CreateDraftAsync("Alpha");
            var two = await vm.CreateDraftAsync("Beta");
            await vm.CreateDraftAsync("Alpine");
            var library = new LibraryView();
            window = new Window { Content = library };
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            Assert.IsFalse(IsWindowVisible(hwnd));
            var cards = Field<GridView>(library, "Cards");
            // Realize the compiled template in an unactivated HWND; no desktop input is sent.
            cards.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel/></ItemsPanelTemplate>");
            var compact = Field<ToggleButton>(library, "CompactToggle");
            var trash = Field<ToggleButton>(library, "TrashToggle");
            var search = Field<TextBox>(library, "SearchBox");
            var count = Field<TextBlock>(library, "SelectionCount");
            var batch = Field<Button>(library, "BatchActionButton");
            var toolbar = Field<StackPanel>(library, "SelectionToolbar");
            LibraryCard[] LibraryCards() => vm.Library.Select(item => new LibraryCard(item.Id, item.Name, item.Summary, "Saved", new([], "", ""))
                { PreferencesLoaded = true, Playback = new() { RepeatUntilStopped = item.Id == one.RecordingId } }).ToArray();
            library.SetCards(LibraryCards());
            LayoutControl(library, 1100, 800);
            await Task.Delay(1);
            Assert.AreEqual(ListViewSelectionMode.None, cards.SelectionMode);
            Assert.IsTrue(cards.IsItemClickEnabled);
            Assert.IsFalse(cards.IsMultiSelectCheckBoxEnabled, "Selection is owned by explicit checkboxes, never card bodies.");
            Assert.AreEqual(Visibility.Collapsed, toolbar.Visibility);
            var opened = new List<LibraryCard>();
            var played = new List<LibraryCard>();
            var options = new List<LibraryCard>();
            library.OpenRequested += (_, card) => opened.Add(card);
            library.PlayRequested += (_, card) => played.Add(card);
            library.PlaybackOptionsRequested += (_, card) => options.Add(card);
            foreach (var dense in new[] { false, true })
            {
                compact.IsChecked = dense;
                LayoutControl(library, 1100, 800);
                await Task.Delay(1);
                var first = (LibraryCard)cards.Items[0];
                Assert.AreEqual(dense ? Visibility.Collapsed : Visibility.Visible, first.ArtVisibility);
                var check = CardControl<CheckBox>(0, "SelectionCheckBox");
                Assert.AreEqual(Visibility.Visible, check.Visibility);
                Assert.AreEqual("Select " + first.Name, AutomationProperties.GetName(check));
                Toggle(0); Toggle(1);
                Assert.AreEqual("2 selected · 3 visible", count.Text);
                Assert.AreEqual(Visibility.Visible, toolbar.Visibility);
                Assert.IsTrue(batch.IsEnabled);
                Toggle(0);
                Assert.AreEqual(1, library.SelectedCards.Count);
                Assert.AreEqual(((LibraryCard)cards.Items[1]).Key, library.SelectedCards.Single().Key);
                Toggle(0);
                var openedBefore = opened.Count;
                var playedBefore = played.Count;
                var optionsBefore = options.Count;
                Invoke(CardControl<Button>(0, "CardPlayButton"));
                Assert.AreSame(first, played.Last());
                Assert.HasCount(openedBefore, opened, "Explicit Play must not open the card.");
                Invoke(CardControl<Button>(0, "CardPlaybackOptionsButton"));
                Assert.AreSame(first, options.Last());
                Assert.HasCount(playedBefore + 1, played, "Changing options must not play.");
                Assert.HasCount(optionsBefore + 1, options);
                Invoke((GridViewItem)cards.ContainerFromIndex(0));
                Assert.AreSame(first, opened.Last(), "A card body still opens while checkboxes are selected.");
                Assert.AreEqual(2, library.SelectedCards.Count, "Body, Play and options never change the batch.");
                Toggle(2);
                Assert.HasCount(openedBefore + 1, opened, "Checkbox activation never opens a card.");
                library.ClearSelection();
                Assert.AreEqual(Visibility.Collapsed, toolbar.Visibility);
                Assert.AreEqual(ToggleState.Off, TogglePeer(0).ToggleState);
                var looping = cards.Items.Cast<LibraryCard>().Single(card => (Guid)card.Key == one.RecordingId);
                StringAssert.Contains(looping.PlaybackSummary, "Until stopped");
                StringAssert.Contains(looping.PlaybackSummary, "3s");
                StringAssert.Contains(looping.PlayLabel, "real input");
            }

            Call(library, "SelectAll_Click", library, new RoutedEventArgs());
            search.Text = "Al";
            Assert.AreEqual(2, cards.Items.Count); Assert.AreEqual(2, library.SelectedCards.Count);
            Assert.IsTrue(library.SelectedCards.All(item => item.Name.StartsWith("Al")));
            StringAssert.Contains(Field<TextBlock>(library, "SelectionScope").Text, "Hidden recordings are deselected");
            search.Text = "Beta";
            Assert.AreEqual(0, library.SelectedCards.Count, "Hidden selections are discarded across repeated query changes.");
            Assert.IsFalse(batch.IsEnabled);
            Toggle(0);
            IReadOnlyList<LibraryCard>? requested = null;
            library.DeleteRequested += (_, values) => requested = values;
            Invoke(batch);
            Assert.AreEqual(two.RecordingId, (Guid)requested!.Single().Key);
            var result = await vm.DeleteRecordingsAsync(requested!.Select(item => (Guid)item.Key));
            Assert.AreEqual(1, result.Succeeded.Count);
            library.SetCards(LibraryCards());
            Assert.AreEqual(0, library.SelectedCards.Count); Assert.IsFalse(batch.IsEnabled);
            await vm.RefreshTrashAsync();
            library.SetTrashCards(vm.Trash.Select(item => new LibraryCard(item.Metadata.Id, item.Metadata.Name, item.Metadata.Summary,
                "Deleted", new([], "In local trash", "Restore to open")) { IsDeleted = true }).ToArray());
            library.SetUndoDeleteCount(vm.LastDeleted.Count);
            Assert.AreEqual(Visibility.Visible, Field<Button>(library, "UndoDeleteButton").Visibility);
            trash.IsChecked = true;
            Assert.AreEqual("Deleted recordings", AutomationProperties.GetName(cards));
            Assert.AreEqual("Restore selected", batch.Content);
            Assert.IsFalse(cards.IsItemClickEnabled);
            var trashed = (LibraryCard)cards.Items.Single();
            Assert.AreEqual(Visibility.Collapsed, trashed.RenameVisibility);
            Assert.AreEqual(Visibility.Collapsed, CardControl<Button>(0, "CardPlayButton").Visibility);
            Assert.AreEqual(Visibility.Collapsed, CardControl<Button>(0, "CardPlaybackOptionsButton").Visibility);
            Toggle(0);
            IReadOnlyList<LibraryCard>? restore = null;
            library.RestoreRequested += (_, values) => restore = values;
            Invoke(batch);
            Assert.AreEqual(two.RecordingId, (Guid)restore!.Single().Key);
            await vm.RestoreRecordingsAsync(restore!.Select(item => (Guid)item.Key));
            trash.IsChecked = false;
            search.Text = "";
            library.SetCards(LibraryCards());
            Assert.AreEqual(0, library.SelectedCards.Count, "Changing between trash and the library clears the batch.");
            Call(library, "SelectAll_Click", library, new RoutedEventArgs());
            Assert.AreEqual(3, library.SelectedCards.Count);
            library.ClearSelection();
            Assert.IsTrue(cards.IsItemClickEnabled);
            Call(library, "ResizeCards", 420d);
            Assert.AreEqual(Orientation.Vertical, Field<StackPanel>(library, "LibraryActions").Orientation);
            Assert.AreEqual(Orientation.Vertical, Field<StackPanel>(library, "SelectionActions").Orientation);
            Assert.IsTrue(cards.Items.Cast<LibraryCard>().All(card => card.ActionsRow == 2));
            Call(library, "ResizeCards", 900d);
            Assert.AreEqual(Orientation.Horizontal, Field<StackPanel>(library, "LibraryActions").Orientation);
            Assert.IsTrue(cards.Items.Cast<LibraryCard>().All(card => card.ActionsRow == 1));
            foreach (var dense in new[] { false, true })
            {
                compact.IsChecked = dense;
                LayoutControl(library, 420, 800);
                await Task.Delay(1);
                var check = CardControl<CheckBox>(0, "SelectionCheckBox", 420);
                var summary = CardControl<TextBlock>(0, "CardPlaybackSummary", 420);
                var body = (Grid)VisualTreeHelper.GetParent(check);
                Assert.AreEqual(32d, check.MinWidth);
                Assert.IsTrue(check.ActualWidth <= 40, $"Contentless checkbox should stay compact, not reserve {check.ActualWidth}px.");
                Assert.IsTrue(body.ColumnDefinitions[0].ActualWidth <= 40);
                Assert.IsTrue(summary.ActualWidth >= 200, $"Narrow card text lost its width: {summary.ActualWidth}px.");
            }
            Assert.IsFalse(IsWindowVisible(hwnd));

            T CardControl<T>(int index, string name, double width = 1100) where T : FrameworkElement
            {
                LayoutControl(library, width, 800);
                var container = (GridViewItem)cards.ContainerFromIndex(index);
                Assert.IsNotNull(container, "Exercise a realized compiled card.");
                container.ApplyTemplate();
                LayoutControl(library, width, 800);
                var found = LibraryDescendants(container).OfType<T>().SingleOrDefault(element => element.Name == name);
                Assert.IsNotNull(found, $"Missing {name}: " + string.Join(", ", LibraryDescendants(container).OfType<FrameworkElement>().Select(element => element.GetType().Name + ":" + element.Name)));
                return found;
            }
            IToggleProvider TogglePeer(int index) => (IToggleProvider)FrameworkElementAutomationPeer
                .CreatePeerForElement(CardControl<CheckBox>(index, "SelectionCheckBox")).GetPattern(PatternInterface.Toggle);
            void Toggle(int index) => TogglePeer(index).Toggle();
            static void Invoke(FrameworkElement control) => ((IInvokeProvider)FrameworkElementAutomationPeer
                .CreatePeerForElement(control).GetPattern(PatternInterface.Invoke)).Invoke();
        }
        finally { window?.Close(); temporary.Delete(recursive: true); }
        await CheckLibraryPreferencePublication();
        await CheckPreferenceCommitAfterWindowClose();
        await CheckPreferenceCommitAfterRejectedClose();
        await CheckStoppedRunCommands();
    }

    private static async Task CheckLibraryPreferencePublication()
    {
        foreach (var scenario in new[] { "document-failure", "committed-cancellation", "preference-failure", "preference-cancellation" })
        {
            var store = new RunTestLibrary();
            var engine = new FakePlaybackEngine();
            using var vm = new MainWindowViewModel(new FakeRecordEngine(), engine, store);
            var target = await vm.CreateDraftAsync("Requested");
            target.AddEvent(new KeyboardEvent(Windows.System.VirtualKey.A, false));
            await vm.SaveRecordingAsync(target);
            await vm.CreateDraftAsync("Other");
            vm.SelectedTabIndex = vm.MacroTabs.IndexOf(target);
            RunLease? optionLease = null;
            var preferences = new RunPreferences(new MemoryRunPreferenceStore { BeforeSave = () =>
            {
                if (scenario == "preference-failure") throw new IOException("preference save unavailable");
                if (scenario == "preference-cancellation") throw new OperationCanceledException();
                if (scenario == "committed-cancellation") optionLease!.Cancel();
                return Task.CompletedTask;
            } });
            await preferences.InitializeAsync();
            var window = new MainWindow(vm, false, preferences);
            SetLibraryShellField(window, "_initialized", true);
            SetLibraryShellField(window, "_libraryVisible", true);
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            try
            {
                var library = Field<LibraryView>(window, "Library");
                library.SetCards(vm.Library.Select(item => new LibraryCard(item.Id, item.Name, item.Summary, "Saved", new([], "", ""))
                    { PreferencesLoaded = true }).ToArray());
                var search = Field<TextBox>(library, "SearchBox");
                search.Text = "Requested";
                Field<ToggleButton>(library, "CompactToggle").IsChecked = true;
                Call(window, "RefreshShell");
                var cards = Field<GridView>(library, "Cards");
                cards.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                    "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel/></ItemsPanelTemplate>");
                LayoutControl(Field<Grid>(window, "RootGrid"), 1100, 800);
                await Task.Delay(1);
                var shown = (LibraryCard)cards.Items.Single();
                shown.IsSelected = true;
                Call(library, "UpdateSelection");
                var before = shown.Playback;
                var changed = new PlaybackOptions { RepeatUntilStopped = true, Countdown = TimeSpan.Zero, Speed = 2 };
                store.BeforeSave = () => throw new IOException("document save unavailable");
                var invoked = false;
                await window.ExecuteLibraryCommandAsync(target.RecordingId, false, async (macro, _, lease) =>
                {
                    invoked = true;
                    optionLease = lease;
                    macro.AddEvent(new KeyboardEvent(Windows.System.VirtualKey.B, false));
                    await DirectRunPreparation.EditPlaybackOptionsAsync(preferences, lease, macro.RecordingId,
                        _ => Task.FromResult<PlaybackOptions?>(changed));
                    await (Task)Call(window, "SaveTargetAsync", macro)!;
                });
                var committed = scenario is "document-failure" or "committed-cancellation";
                Assert.IsTrue(invoked);
                Assert.AreEqual(committed ? 1L : 0L, preferences.Revision, scenario);
                Assert.AreEqual(committed ? changed : before, preferences.PlaybackFor(target.RecordingId));
                Assert.AreSame(shown, cards.Items.Single(), "Updating settings must not recreate the selected/focused card.");
                Assert.AreEqual(preferences.PlaybackFor(target.RecordingId), shown.Playback, scenario);
                Assert.AreEqual(RunSettingsPresentation.PlaybackSummary(true, shown.Playback), shown.PlaybackSummary);
                var summary = LibraryDescendants((GridViewItem)cards.ContainerFromIndex(0)).OfType<TextBlock>()
                    .Single(element => element.Name == "CardPlaybackSummary");
                Assert.AreEqual(shown.PlaybackSummary, summary.Text, "The compiled visible label must follow the committed preference.");
                StringAssert.Contains(shown.PlayLabel, shown.PlaybackSummary);
                Assert.IsTrue(shown.IsSelected);
                Assert.AreEqual("Requested", search.Text);
                Assert.IsTrue(Field<ToggleButton>(library, "CompactToggle").IsChecked);
                var feedback = Field<TextBlock>(library, "OperationMessage").Text;
                StringAssert.Contains(feedback, scenario == "document-failure" ? "document save unavailable"
                    : scenario == "preference-failure" ? "preference save unavailable" : "Playback options cancelled");
                search.Text = "";
                Assert.AreEqual(shown.Playback, cards.Items.Cast<LibraryCard>().Single(card => (Guid)card.Key == target.RecordingId).Playback,
                    "Filtering later must retain the updated backing card settings.");
                Assert.AreSame(target, vm.ActiveMacro);
                Assert.AreEqual(0, engine.Starts);
                Assert.IsNull(Field<RunController?>(window, "_controller"));
                Assert.IsFalse(Field<bool>(window, "_mainHiddenForRun"));
                Assert.IsFalse(IsWindowVisible(hwnd));
            }
            finally { SetLibraryShellField(window, "_allowClose", true); window.Close(); }
        }
    }

    private static async Task CheckPreferenceCommitAfterWindowClose()
    {
        var store = new RunTestLibrary();
        var engine = new FakePlaybackEngine();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), engine, store);
        var target = await vm.CreateDraftAsync("Requested");
        var blocked = new PreparationBlock();
        var preferences = new RunPreferences(new MemoryRunPreferenceStore { BeforeSave = blocked.WaitAsync });
        await preferences.InitializeAsync();
        var window = new MainWindow(vm, false, preferences);
        SetLibraryShellField(window, "_initialized", true);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        try
        {
            var library = Field<LibraryView>(window, "Library");
            library.SetCards([new(target.RecordingId, target.Name, "", "Saved", new([], "", "")) { PreferencesLoaded = true }]);
            var shown = (LibraryCard)Field<GridView>(library, "Cards").Items.Single();
            var notifications = 0;
            shown.PropertyChanged += (_, _) => notifications++;
            var changed = new PlaybackOptions { RepeatUntilStopped = true, Countdown = TimeSpan.Zero };
            var operation = window.ExecuteLibraryCommandAsync(target.RecordingId, false, (macro, _, lease) =>
                DirectRunPreparation.EditPlaybackOptionsAsync(preferences, lease, macro.RecordingId,
                    _ => Task.FromResult<PlaybackOptions?>(changed)));
            await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            SetLibraryShellField(window, "_allowClose", true);
            window.Close();
            blocked.Release.SetResult();
            await operation;
            Assert.IsTrue(Field<bool>(window, "_closed"));
            Assert.AreEqual(1L, preferences.Revision);
            Assert.AreEqual(changed, preferences.PlaybackFor(target.RecordingId));
            Assert.AreEqual(0, notifications, "A late commit must not update disposed card bindings.");
            Assert.AreEqual(0, engine.Starts);
            Assert.IsFalse(IsWindowVisible(hwnd));
        }
        finally
        {
            blocked.Release.TrySetResult();
            if (!Field<bool>(window, "_closed")) { SetLibraryShellField(window, "_allowClose", true); window.Close(); }
        }
    }

    private static async Task CheckPreferenceCommitAfterRejectedClose()
    {
        var store = new RunTestLibrary();
        var engine = new FakePlaybackEngine();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), engine, store);
        var target = await vm.CreateDraftAsync("Requested");
        var preferenceWrite = new PreparationBlock();
        var documentWrite = new PreparationBlock();
        var keepOpen = new PreparationBlock();
        var preferences = new RunPreferences(new MemoryRunPreferenceStore { BeforeSave = preferenceWrite.WaitAsync });
        await preferences.InitializeAsync();
        var window = new MainWindow(vm, false, preferences);
        SetLibraryShellField(window, "_initialized", true);
        SetLibraryShellField(window, "_libraryVisible", true);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        Task? optionsOperation = null, closeOperation = null;
        try
        {
            var library = Field<LibraryView>(window, "Library");
            library.SetCards([new(target.RecordingId, target.Name, "", "Saved", new([], "", "")) { PreferencesLoaded = true }]);
            var search = Field<TextBox>(library, "SearchBox");
            search.Text = "Requested";
            Field<ToggleButton>(library, "CompactToggle").IsChecked = true;
            Call(window, "RefreshShell");
            var cards = Field<GridView>(library, "Cards");
            cards.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel/></ItemsPanelTemplate>");
            LayoutControl(Field<Grid>(window, "RootGrid"), 1100, 800);
            await Task.Delay(1);
            var shown = (LibraryCard)cards.Items.Single();
            shown.IsSelected = true;
            Call(library, "UpdateSelection");
            var before = shown.Playback;
            var changed = new PlaybackOptions { RepeatUntilStopped = true, Countdown = TimeSpan.Zero, Speed = 2 };
            optionsOperation = window.ExecuteLibraryCommandAsync(target.RecordingId, false, async (macro, _, lease) =>
            {
                macro.AddEvent(new KeyboardEvent(Windows.System.VirtualKey.A, false));
                await DirectRunPreparation.EditPlaybackOptionsAsync(preferences, lease, macro.RecordingId,
                    _ => Task.FromResult<PlaybackOptions?>(changed));
            });
            await preferenceWrite.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            store.BeforeSave = async () => { await documentWrite.WaitAsync(); throw new IOException("close document save unavailable"); };
            string? closeFailure = null;
            closeOperation = window.CloseSafelyAsync(async error =>
            {
                closeFailure = error;
                await keepOpen.WaitAsync();
                return false;
            });
            await documentWrite.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            preferenceWrite.Release.SetResult();
            await optionsOperation;
            Assert.IsTrue(Field<bool>(window, "_closing"));
            Assert.AreEqual(changed, preferences.PlaybackFor(target.RecordingId));
            Assert.AreEqual(before, shown.Playback, "Closing deliberately defers updates to the current card bindings.");
            documentWrite.Release.SetResult();
            await keepOpen.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual("close document save unavailable", closeFailure);
            keepOpen.Release.SetResult();
            await closeOperation;

            Assert.IsFalse(Field<bool>(window, "_closing"));
            Assert.IsFalse(Field<bool>(window, "_closed"));
            Assert.IsFalse(Field<bool>(window, "_allowClose"));
            Assert.AreEqual(1L, preferences.Revision);
            Assert.AreEqual(changed, shown.Playback, "Keep open must publish settings committed while closing without another user action or timer tick.");
            Assert.AreSame(shown, cards.Items.Single());
            var summary = LibraryDescendants((GridViewItem)cards.ContainerFromIndex(0)).OfType<TextBlock>()
                .Single(element => element.Name == "CardPlaybackSummary");
            Assert.AreEqual(RunSettingsPresentation.PlaybackSummary(true, changed), summary.Text);
            StringAssert.Contains(shown.PlayLabel, summary.Text);
            Assert.IsTrue(shown.IsSelected);
            Assert.AreEqual("Requested", search.Text);
            Assert.IsTrue(Field<ToggleButton>(library, "CompactToggle").IsChecked);
            Assert.IsTrue(library.IsEnabled);
            Assert.IsTrue(Field<Button>(window, "PlaybackOptionsButton").IsEnabled);
            StringAssert.Contains(Field<TextBlock>(window, "StatusText").Text, "close document save unavailable");
            Assert.IsTrue(target.IsDirty, "Rejected close must retain the unsaved document.");
            Assert.AreEqual(0, engine.Starts);
            Assert.IsFalse(vm.IsRecording);
            Assert.IsNull(Field<RunController?>(window, "_controller"));
            Assert.IsFalse(Field<bool>(window, "_mainHiddenForRun"));
            Assert.IsFalse(IsWindowVisible(hwnd));
        }
        finally
        {
            preferenceWrite.Release.TrySetResult(); documentWrite.Release.TrySetResult(); keepOpen.Release.TrySetResult();
            if (optionsOperation is not null) await optionsOperation;
            if (closeOperation is not null) await closeOperation;
            SetLibraryShellField(window, "_allowClose", true); window.Close();
        }
    }

    private static async Task CheckStoppedRunCommands()
    {
        foreach (var fail in new[] { false, true })
        {
            var store = new RunTestLibrary();
            var engine = new FakePlaybackEngine();
            using var vm = new MainWindowViewModel(new FakeRecordEngine(), engine, store);
            var macro = vm.ActiveMacro!;
            macro.AddEvent(new KeyboardEvent(Windows.System.VirtualKey.A, false));
            var preferences = new RunPreferences(new MemoryRunPreferenceStore());
            await preferences.InitializeAsync();
            var window = new MainWindow(vm, false, preferences);
            SetLibraryShellField(window, "_initialized", true);
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var blocked = new PreparationBlock();
            try
            {
                vm.SetEmergencyStopAvailability(true);
                var run = Field<ShellRunLifetime>(window, "_runLifetime").Begin();
                SetLibraryShellField(window, "_activeRun", run);
                SetLibraryShellField(window, "_runMacro", macro);
                SetLibraryShellField(window, "_isRecordingRun", true);
                var timer = Field<DispatcherTimer>(window, "_runTimer");
                // Model a run that has already ended. Never create/show its controller.
                Assert.IsNull(Field<RunController?>(window, "_controller"));
                Assert.IsFalse(Field<bool>(window, "_mainHiddenForRun"));
                store.BeforeSave = async () =>
                {
                    await blocked.WaitAsync();
                    if (fail) throw new IOException("final save unavailable");
                };
                timer.Start();
                var completion = window.FinishExternallyStoppedRunAsync();
                await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.IsFalse(Field<ContentControl>(window, "EditorHost").IsEnabled);
                Assert.IsFalse(Field<Button>(window, "RecordButton").IsEnabled);
                blocked.Release.SetResult();
                await completion;
                Assert.IsFalse(timer.IsEnabled, "Completion stops the timer; controls must already be refreshed.");
                Assert.IsFalse(Field<bool>(window, "_stopping"));
                Assert.IsFalse(Field<bool>(window, "_savingRun"));
                Assert.IsNull(Field<RunLease?>(window, "_activeRun"));
                Assert.IsTrue(Field<ContentControl>(window, "EditorHost").IsEnabled);
                Assert.IsTrue(Field<LibraryView>(window, "Library").IsEnabled);
                foreach (var name in new[] { "RecordButton", "PlayButton", "RecordingOptionsButton", "PlaybackOptionsButton", "DocumentButton", "ImportButton" })
                    Assert.IsTrue(Field<Button>(window, name).IsEnabled, name);
                if (fail) StringAssert.Contains(Field<TextBlock>(window, "StatusText").Text, "final save unavailable");
                Assert.AreEqual(0, engine.Starts);
                Assert.IsFalse(vm.IsRecording);
                Assert.IsNull(Field<RunController?>(window, "_controller"));
                Assert.IsFalse(Field<bool>(window, "_mainHiddenForRun"));
                Assert.IsFalse(IsWindowVisible(hwnd));
            }
            finally { blocked.Release.TrySetResult(); SetLibraryShellField(window, "_allowClose", true); window.Close(); }
        }
    }

    private static void SetLibraryShellField(MainWindow window, string name, object? value)
        => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    private static IEnumerable<DependencyObject> LibraryDescendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in LibraryDescendants(child)) yield return descendant;
        }
    }
}
