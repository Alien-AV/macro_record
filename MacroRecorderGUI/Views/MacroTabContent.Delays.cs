using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace MacroRecorderGUI.Views;

public sealed partial class MacroTabContent
{
    private InputEvent? _leadingDelayAnchor;

    private void LeadingDelay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecordedAction action }) SelectLeadingDelay(action);
    }

    private void SelectLeadingDelay(RecordedAction action)
    {
        if (_macro is null || _editor is null || !TryCommitPendingEdits()) return;
        var anchor = action.First;
        RefreshEditor();
        if (_editor.Projection.ActionAt(_macro.Events.IndexOf(anchor)) is not { } current) return;
        action = current;
        ActionsList.SelectedItem = action;
        _leadingDelayAnchor = anchor;
        _rawEvent = anchor; _rawSelection = [anchor];
        _actionEdits?.SelectDelay(anchor);
        SetRawOpen(false);
        UpdateInspector();
        ShowPane(true);
    }

    private void ActionBody_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_leadingDelayAnchor is null || !TryCommitPendingEdits()) return;
        _leadingDelayAnchor = null;
        _actionEdits?.Select(Selected, reset: true);
        UpdateInspector();
        UpdateSelectionScope();
    }

    private void UpdateDelayInspector(RecordedAction action)
    {
        var delay = _leadingDelayAnchor;
        DelayFields.Visibility = delay is null && action.Kind != ActionKind.Delay ? Visibility.Collapsed : Visibility.Visible;
        if (delay is null && action.First is DelayEvent fixedDelay)
        {
            EditorPathHost.Visibility = InputVisual.Visibility = Visibility.Collapsed;
            TimingSummary.Text = $"Wait {TimeText.Seconds(fixedDelay.DurationMicroseconds)} s, then continue. Zero keeps this step without waiting.";
        }
        if (delay is null) return;
        _actionEdits?.SelectDelay(delay);
        SelectedTitle.Text = "Delay";
        DurationFields.Visibility = DestinationFields.Visibility = Visibility.Collapsed;
        EditorPathHost.Visibility = InputVisual.Visibility = Visibility.Collapsed;
        WaitConditionHost.Visibility = ApplyCondition.Visibility = Visibility.Collapsed;
        TimingSummary.Text = $"Wait {TimeText.Seconds(delay.TimeSinceLastEvent)} s, then {action.Name}.";
        SelectionScope.Text = "Delay selected";
        ToolTipService.SetToolTip(DeleteButton, "Delete this delay (Delete)");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(DeleteButton, "Delete this delay");
    }
}
