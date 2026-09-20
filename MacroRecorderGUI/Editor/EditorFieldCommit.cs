using MacroRecorderGUI.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace MacroRecorderGUI.Views;

public sealed partial class MacroTabContent
{
    private ActionField? ActionFieldFor(object control) => ReferenceEquals(control, WaitInput) ? ActionField.Wait
        : ReferenceEquals(control, DurationInput) ? ActionField.Duration
        : ReferenceEquals(control, DestinationX) ? ActionField.DestinationX
        : ReferenceEquals(control, DestinationY) ? ActionField.DestinationY : null;

    /// <summary>Called before disabling, saving, navigating or snapshotting this editor.</summary>
    public bool TryCommitPendingEdits() => CommitActionFields();

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

    private void ActionField_LosingFocus(UIElement sender, LosingFocusEventArgs e)
    {
        if (ActionFieldFor(sender) is not { } field) return;
        // X/Y are one validation unit. Moving between them must keep both drafts intact.
        if (field is ActionField.DestinationX or ActionField.DestinationY
            && ActionFieldFor(e.NewFocusedElement) is ActionField.DestinationX or ActionField.DestinationY) return;
        CommitActionFields(field);
    }

    private void ActionField_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || ActionFieldFor(sender) is not { } field) return;
        CommitActionFields(field); e.Handled = true;
    }
}
