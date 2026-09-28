using MacroRecorderGUI.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace MacroRecorderGUI.Views;

public sealed partial class MacroTabContent
{
    private ActionField? ActionFieldFor(object control) => ReferenceEquals(control, WaitInput)
        ? _leadingDelayAnchor is null && Selected?.Kind == ActionKind.Delay ? ActionField.FixedDelay : ActionField.Wait
        : ReferenceEquals(control, DurationInput) ? ActionField.Duration
        : ReferenceEquals(control, DestinationX) ? ActionField.DestinationX
        : ReferenceEquals(control, DestinationY) ? ActionField.DestinationY : null;

    /// <summary>Called before disabling, saving, navigating or snapshotting this editor.</summary>
    public bool TryCommitPendingEdits()
    {
        if (_modalConditionEditor is not null || _authoringDialogOpen)
        {
            _modalConditionEditor?.CancelTest();
            Status = "Close the Add action dialog before starting another command.";
            return false;
        }
        _conditionEditor?.CancelTest();
        return CanLeaveRawDraft() && CommitCondition() && CommitActionFields();
    }

    private bool CommitActionFields(ActionField? field = null, bool refresh = true)
    {
        if (_committingFields || _sync || _actionEdits is null || !_actionEdits.HasDrafts) return true;
        if (_dragging) return false;
        _committingFields = true;
        try
        {
            var committed = field is { } value ? _actionEdits.TryCommit(value) : _actionEdits.TryCommitAll();
            if (refresh) RefreshEditor();
            Status = _actionEdits.Error ?? "Changes applied. Undo is available.";
            return committed;
        }
        finally { _committingFields = false; }
    }

    private void ActionField_LostFocus(object sender, RoutedEventArgs e)
    {
        var next = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        if (_navigatingView || IsViewNavigation(next)) return;
        if (ActionFieldFor(sender) is not { } field) return;
        // X/Y are one validation unit. Moving between them must keep both drafts intact.
        if (field is ActionField.DestinationX or ActionField.DestinationY
            && ActionFieldFor(next!) is ActionField.DestinationX or ActionField.DestinationY) return;
        // Rebuilding selection during LosingFocus interrupts WinUI's focus transaction.
        // Commit only after the field has actually lost focus.
        CommitActionFields(field);
    }

    private void ActionField_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || ActionFieldFor(sender) is not { } field) return;
        CommitActionFields(field); e.Handled = true;
    }
}
