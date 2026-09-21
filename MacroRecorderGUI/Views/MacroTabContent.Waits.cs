using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MacroRecorderGUI.Views;

public sealed partial class MacroTabContent
{
    private WaitConditionEditor? _conditionEditor;
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

    private async Task AddWaitAsync(bool replaceDelay)
    {
        if (_editor is null || _macro is null || !TryCommitPendingEdits()) return;
        var editor = _editor;
        var selected = Selected;
        if (replaceDelay && selected is null) { Status = "Select the action whose fixed delay should be replaced."; return; }
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
                if (replaceDelay) editor.ReplaceDelayWithWait(selected!, condition); else editor.InsertWait(selected, condition);
            }
            catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
            { fields.Feedback.Text = error.Message; args.Cancel = true; }
        };
        await dialog.ShowAsync();
        if (!_disposed && ReferenceEquals(_editor, editor)) RefreshEditor(resetDrafts: true);
    }

    private void SimulateCondition_Click(object sender, RoutedEventArgs e)
    {
        StopPreview();
        if (_preview?.SimulateSatisfied(_previewPosition.Time) == true) { UpdatePreviewFrame(); DrawPath(); }
    }
}
