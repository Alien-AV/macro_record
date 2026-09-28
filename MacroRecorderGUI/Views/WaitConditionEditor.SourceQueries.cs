using MacroRecorder.Waiting;
using MacroRecorderGUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ProtobufGenerated;

namespace MacroRecorderGUI.Views;

internal sealed partial class WaitConditionEditor
{
    private static readonly SemaphoreSlim SourceQuerySlot = new(1, 1);
    private CancellationTokenSource? _sourceQuery;
    private Button? _sourceQueryButton;
    private long _sourceQueryGeneration;
    private IWaitSourceQueries _sourceQueries = new WindowsWaitSourceQueries();
    private WaitProcessChoice? _chosenProcess;
    internal ComboBox AccessibilityChoices = null!, ProcessChoices = null!, ModuleChoices = null!, LanguageChoices = null!;
    internal readonly TextBlock ProcessChoiceWarning = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Visibility = Visibility.Collapsed };
    private Button _chooseElement = null!, _chooseProcess = null!, _chooseModule = null!, _chooseLanguage = null!;

    internal void UseSourceQueries(IWaitSourceQueries queries) => _sourceQueries = queries;

    private void CreateAccessibilityChoiceControls()
    {
        _chooseElement = SourceAsyncButton("Choose accessible element…", LoadAccessibilityChoicesAsync);
        AccessibilityChoices = SourceChoicesBox("Accessible elements", choice =>
        {
            if (choice is not AccessibilityChoice value) return;
            _populating = true;
            try
            {
                ElementId.Text = value.Element.AutomationId; ElementType.Text = value.Element.ControlType.ToString(System.Globalization.CultureInfo.InvariantCulture);
                foreach (var row in _ancestorFields.Select(value => value.Row).ToArray()) RemoveAncestor(row);
                foreach (var ancestor in value.Ancestors) AddAncestor(ancestor);
            }
            finally { _populating = false; }
            Change();
        });
        AccessibilityChoices.DisplayMemberPath = nameof(AccessibilityChoice.Label);
        _accessibility.Children.Add(_chooseElement); _accessibility.Children.Add(AccessibilityChoices);
        foreach (var field in new[] { Executable, Class, Title }) field.TextChanging += (_, _) => AccessibilityChoices.Visibility = Visibility.Collapsed;
        TitleRule.SelectionChanged += (_, _) => AccessibilityChoices.Visibility = Visibility.Collapsed;
        IgnoreCase.Checked += (_, _) => AccessibilityChoices.Visibility = Visibility.Collapsed;
        IgnoreCase.Unchecked += (_, _) => AccessibilityChoices.Visibility = Visibility.Collapsed;
    }

    private void CreateProcessChoiceControls()
    {
        _chooseProcess = SourceAsyncButton("Choose running application…", LoadProcessesAsync);
        _chooseProcess.IsEnabled = _sourcePreferences.Options.MemoryEnabled;
        ProcessChoices = SourceChoicesBox("Running applications", choice =>
        {
            if (choice is not WaitProcessChoice value) return;
            MemoryExecutable.Text = value.ExecutablePath; _chosenProcess = value;
        });
        ProcessChoices.DisplayMemberPath = nameof(WaitProcessChoice.Label);
        _memory.Children.Add(_chooseProcess); _memory.Children.Add(ProcessChoices); _memory.Children.Add(ProcessChoiceWarning);
    }

    private void CreateModuleChoiceControls(StackPanel host)
    {
        _chooseModule = SourceAsyncButton("Choose loaded module…", LoadModulesAsync);
        _chooseModule.IsEnabled = _sourcePreferences.Options.MemoryEnabled;
        ModuleChoices = SourceChoicesBox("Loaded modules", choice =>
        {
            if (choice is not WaitModuleChoice value) return;
            _populating = true;
            try { MemoryModulePath.Text = value.Path; MemoryModuleVersion.Text = value.FileVersion; }
            finally { _populating = false; }
            Change();
        });
        ModuleChoices.DisplayMemberPath = nameof(WaitModuleChoice.Label);
        host.Children.Add(_chooseModule); host.Children.Add(ModuleChoices);
        MemoryExecutable.TextChanging += (_, _) => { _chosenProcess = null; ModuleChoices.Visibility = Visibility.Collapsed; };
    }

    private void CreateLanguageChoiceControls()
    {
        _chooseLanguage = SourceAsyncButton("List installed languages", LoadLanguagesAsync);
        LanguageChoices = SourceChoicesBox("Installed OCR languages", choice => { if (choice is string value) OcrLanguage.Text = value; });
        _ocr.Children.Add(_chooseLanguage); _ocr.Children.Add(LanguageChoices);
    }

    private ComboBox SourceChoicesBox(string label, Action<object> apply)
    {
        var box = new ComboBox { Header = label, HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(box, label);
        box.SelectionChanged += (_, _) => { if (!_populating && box.SelectedItem is { } choice) apply(choice); };
        return box;
    }

    internal Task LoadAccessibilityChoicesAsync()
    {
        var selector = ReadSourceTarget(_accessibilityTemplate.Target);
        // Validate only the selector: an incomplete text predicate must not block picking.
        var check = WaitValidation.NewWindow(); check.Window.Target = selector;
        try
        {
            WaitValidation.Validate(check);
            if (selector.AnyMatch) throw new ArgumentException("Choose a single window before listing its accessible elements.");
        }
        catch (ArgumentException error) { Feedback.Text = error.Message; return Task.CompletedTask; }
        return LoadSourceChoicesAsync(_chooseElement, "Choose accessible element…", AccessibilityChoices,
            token => _sourceQueries.AccessibilityAsync(selector, token), 512);
    }
    internal Task LoadProcessesAsync() => MemoryQueriesAllowed()
        ? LoadSourceChoicesAsync(_chooseProcess, "Choose running application…", ProcessChoices,
            token => _sourceQueries.ProcessesAsync(token), 4096, allowPartial: true) : Task.CompletedTask;
    internal Task LoadModulesAsync()
    {
        if (!MemoryQueriesAllowed()) return Task.CompletedTask;
        if (_chosenProcess is not { } process)
        { Feedback.Text = "Choose a running application from the list before listing its modules. Module fields can also be entered manually."; return Task.CompletedTask; }
        return LoadSourceChoicesAsync(_chooseModule, "Choose loaded module…", ModuleChoices,
            token => _sourceQueries.ModulesAsync(process, token), 4096);
    }
    internal Task LoadLanguagesAsync()
    {
        if (HasUnsavedOcrInstallation())
        { Feedback.Text = "Save the local OCR installation fields before listing languages."; return Task.CompletedTask; }
        return LoadSourceChoicesAsync(_chooseLanguage, "List installed languages", LanguageChoices,
            token => _sourceQueries.LanguagesAsync(token), 256);
    }

    private async Task LoadSourceChoicesAsync<T>(Button button, string label, ComboBox list,
        Func<CancellationToken, Task<WaitSourceChoices<T>>> query, int maximum, bool allowPartial = false)
    {
        if (_disposed) return;
        if (_sourceQuery is not null) { CancelAdditionalSourceWork(); return; }
        CancelTest();
        var generation = ++_sourceQueryGeneration;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var providerCancellation = new CancellationTokenSource();
        _sourceQuery = deadline;
        _sourceQueryButton = button;
        button.Content = "Cancel choice lookup";
        list.Visibility = Visibility.Collapsed;
        if (ReferenceEquals(list, ProcessChoices)) ProcessChoiceWarning.Visibility = Visibility.Collapsed;
        Feedback.Text = "Loading choices… No condition test is running.";
        var ownsSlot = false;
        Task pending = Task.CompletedTask;
        try
        {
            await SourceQuerySlot.WaitAsync(deadline.Token); ownsSlot = true;
            var request = Task.Run(() => query(providerCancellation.Token)); pending = request;
            var result = await request.WaitAsync(deadline.Token);
            if (_disposed || deadline.IsCancellationRequested || generation != _sourceQueryGeneration) return;
            if (result.Status != ReadStatus.Success)
            { Feedback.Text = result.Detail.Length == 0 ? "Choices could not be loaded. Check the target and try again." : result.Detail; return; }
            if (!result.IsComplete && !allowPartial)
            { Feedback.Text = "The choice lookup was incomplete. " + result.Detail; return; }
            if (result.Items.Count > maximum)
            { Feedback.Text = $"The choice list exceeds the {maximum}-item limit. Use a narrower target or enter the selector manually."; return; }
            _populating = true;
            try { list.ItemsSource = result.Items; list.SelectedIndex = -1; list.Visibility = Visibility.Visible; }
            finally { _populating = false; }
            Feedback.Text = !result.IsComplete && result.Items.Count == 0 ? "No accessible choices could be listed. This does not establish absence."
                : result.Items.Count == 0 ? "No choices found. Check the target or enter its fields manually."
                : $"{result.Items.Count} choices available. Select one to fill the draft; you can still edit every field.";
            if (!string.IsNullOrWhiteSpace(result.Detail)) Feedback.Text += " " + result.Detail;
            else if (!result.IsComplete) Feedback.Text += " Some inaccessible targets were omitted; this list cannot establish absence.";
            if (ReferenceEquals(list, ProcessChoices) && !result.IsComplete)
            {
                ProcessChoiceWarning.Text = string.IsNullOrWhiteSpace(result.Detail)
                    ? "Some inaccessible targets were omitted; this list cannot establish absence." : result.Detail;
                ProcessChoiceWarning.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        { if (!_disposed && generation == _sourceQueryGeneration) Feedback.Text = "Choice lookup timed out. Check the target and try again."; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { if (!_disposed && generation == _sourceQueryGeneration) Feedback.Text = "Choices are unavailable. " + error.Message; }
        finally
        {
            if (ReferenceEquals(_sourceQuery, deadline)) { _sourceQuery = null; _sourceQueryButton = null; }
            if (!ReferenceEquals(_sourceQueryButton, button) || generation == _sourceQueryGeneration) button.Content = label;
            // A timeout can end the UI operation while a provider is finishing.
            // Keep the shared permit until both it and cancellation callbacks end.
            _ = Task.WhenAll(pending, providerCancellation.CancelAsync()).ContinueWith(task =>
            {
                _ = task.Exception; providerCancellation.Dispose(); if (ownsSlot) SourceQuerySlot.Release();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    partial void CancelAdditionalSourceWork()
    {
        if (_sourceQuery is not { } query) return;
        _sourceQueryGeneration++; _sourceQuery = null; _sourceQueryButton = null; query.Cancel();
        if (!_disposed) Feedback.Text = "Choice lookup cancelled.";
    }

    private bool MemoryQueriesAllowed()
    {
        if (_sourcePreferences.Options.MemoryEnabled) return true;
        Feedback.Text = "Enable read-only memory waits on this computer before listing processes or modules.";
        return false;
    }

    private void UpdateMemoryQueryAvailability()
    {
        var enabled = _sourcePreferences.Options.MemoryEnabled;
        _chooseProcess.IsEnabled = _chooseModule.IsEnabled = enabled;
        if (!enabled) { _chosenProcess = null; ProcessChoices.Visibility = ModuleChoices.Visibility = ProcessChoiceWarning.Visibility = Visibility.Collapsed; }
    }
}
