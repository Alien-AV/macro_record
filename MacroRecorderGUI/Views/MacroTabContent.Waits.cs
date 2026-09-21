using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProtobufGenerated;

namespace MacroRecorderGUI.Views;

public sealed partial class MacroTabContent
{
    private WaitConditionEditor? _conditionEditor;
    private WaitConditionEditor? _modalConditionEditor;
    private WaitConditionEvent? _conditionOwner;

    private void UpdateConditionInspector(RecordedAction? action, bool reset)
    {
        var owner = action?.First as WaitConditionEvent;
        if (!ReferenceEquals(owner, _conditionOwner) || reset)
        {
            _conditionEditor?.Dispose(); _conditionEditor = null; _conditionOwner = owner;
            if (owner is not null)
            {
                _conditionEditor = new(owner.Condition);
                _conditionEditor.ApplyStyles((Style)Resources.MergedDictionaries[0]["DesignedEditorField"], (Style)Resources.MergedDictionaries[0]["DesignedEditorButton"]);
            }
            WaitConditionHost.Content = _conditionEditor;
        }
        WaitConditionHost.Visibility = owner is null ? Visibility.Collapsed : Visibility.Visible;
        ApplyCondition.Visibility = owner is null ? Visibility.Collapsed : Visibility.Visible;
        if (owner is not null)
        {
            EditorPathHost.Visibility = InputVisual.Visibility = Visibility.Collapsed;
            TimingSummary.Text = action!.Summary;
            TechnicalDetail.Text = action.TechnicalSummary + "\nConditional duration is unknown; timeout is a maximum.";
        }
    }

    private bool CommitCondition(bool refresh = false)
    {
        if (_committingFields || _sync) return true;
        _conditionEditor?.CancelTest();
        if (_conditionEditor is not { IsDirty: true } fields || _conditionOwner is null || _editor is null || _macro is null) return true;
        _committingFields = true;
        try
        {
            var condition = fields.Read();
            _editor.Refresh();
            var action = _editor.Projection.ActionAt(_macro.Events.IndexOf(_conditionOwner))
                ?? throw new ArgumentException("The condition's action is no longer available.");
            _editor.SetCondition(action, condition); fields.Committed();
            if (refresh) RefreshEditor();
            Status = "Condition saved. Undo is available.";
            return true;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        { fields.Feedback.Text = Status = error.Message; return false; }
        finally { _committingFields = false; }
    }
    private void ApplyCondition_Click(object sender, RoutedEventArgs e) => CommitCondition(refresh: true);
    private async void AddWait_Click(object sender, RoutedEventArgs e) => await AddWaitAsync(replaceDelay: false);
    private async void ReplaceDelay_Click(object sender, RoutedEventArgs e) => await AddWaitAsync(replaceDelay: true);

    private Action<WaitCondition>? PrepareWaitInsertion(bool replaceDelay)
    {
        if (_editor is null || _macro is null) return null;
        var editor = _editor;
        var macro = _macro;
        var anchor = Selected?.First;
        if (!TryCommitPendingEdits()) return null;
        if (replaceDelay && anchor is null) { Status = "Select the action whose fixed delay should be replaced."; return null; }
        return condition =>
        {
            if (_disposed || !ReferenceEquals(_editor, editor) || !ReferenceEquals(_macro, macro))
                throw new ArgumentException("The recording is no longer open in this editor. Reopen the wait command.");
            // Draft commits and deferred refreshes replace projected actions. The
            // selected event remains the command's anchor throughout the dialog.
            RefreshEditor();
            var selected = anchor is null ? null : editor.Projection.ActionAt(macro.Events.IndexOf(anchor))
                ?? throw new ArgumentException("The selected action is no longer available. Reopen the wait command.");
            if (replaceDelay) editor.ReplaceDelayWithWait(selected!, condition); else editor.InsertWait(selected, condition);
        };
    }

    private async Task AddWaitAsync(bool replaceDelay)
    {
        var apply = PrepareWaitInsertion(replaceDelay);
        if (apply is null) return;
        var editor = _editor;
        using var fields = new WaitConditionEditor(WaitValidation.NewWindow());
        fields.ApplyStyles((Style)Resources.MergedDictionaries[0]["DesignedEditorField"], (Style)Resources.MergedDictionaries[0]["DesignedEditorButton"]);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = replaceDelay ? "Replace fixed delay with a condition" : "Wait until…",
            PrimaryButtonText = replaceDelay ? "Replace delay" : "Add wait", CloseButtonText = "Cancel",
            Content = new ScrollViewer { Content = fields, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var condition = fields.Read();
                apply(condition);
            }
            catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
            { fields.Feedback.Text = error.Message; args.Cancel = true; }
        };
        BeginWaitDialog(fields);
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { EndWaitDialog(fields); }
        FinishWaitInsertion(editor!, result);
    }

    private void BeginWaitDialog(WaitConditionEditor fields)
    {
        if (_disposed || _modalConditionEditor is not null) throw new InvalidOperationException("A wait dialog cannot be opened here.");
        _modalConditionEditor = fields;
    }
    private void EndWaitDialog(WaitConditionEditor fields)
    {
        fields.CancelTest();
        if (ReferenceEquals(_modalConditionEditor, fields)) _modalConditionEditor = null;
    }

    private void FinishWaitInsertion(ActionEditor editor, ContentDialogResult result)
    {
        if (!_disposed && ReferenceEquals(_editor, editor)) RefreshEditor(resetDrafts: result == ContentDialogResult.Primary);
    }

    private void SimulateCondition_Click(object sender, RoutedEventArgs e)
    {
        StopPreview();
        if (_preview?.SimulateSatisfied(_previewPosition.Time) == true) { UpdatePreviewFrame(); DrawPath(); }
    }
}
