using MacroRecorderGUI.Event;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace MacroRecorderGUI.Views;

public sealed partial class MacroTabContent
{
    private bool _detailsPane, _navigatingView;
    private Control? _actionFocus, _rawFocus, _sequenceFocus;
    private InputEvent[] _rawSelection = [];

    private static bool Within(DependencyObject? element, DependencyObject ancestor)
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, ancestor)) return true;
        return false;
    }

    private bool IsViewNavigation(DependencyObject? element) =>
        Within(element, SequenceNavigation) || Within(element, DetailsNavigation)
        || Within(element, RawToggle) || Within(element, BackToAction);

    private void Editor_GotFocus(object sender, RoutedEventArgs e) => RememberFocus(e.OriginalSource as DependencyObject);
    private void RememberPaneFocus() => RememberFocus(XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject);
    private void RememberFocus(DependencyObject? element)
    {
        if (IsViewNavigation(element)) return;
        while (element is not null && element is not Control) element = VisualTreeHelper.GetParent(element);
        if (element is not Control control) return;
        if (Within(control, RawContent)) { _rawFocus = control; _detailsPane = true; }
        else if (Within(control, InspectorPanel)) { _actionFocus = control; _detailsPane = true; }
        else if (Within(control, ListPanel)) { _sequenceFocus = control; _detailsPane = false; }
    }

    private void SequenceNavigation_Click(object sender, RoutedEventArgs e) => ShowPane(false);
    private void DetailsNavigation_Click(object sender, RoutedEventArgs e) => ShowPane(true);
    private void ShowPane(bool details)
    {
        if (IsPreviewMode || _modalConditionEditor is not null || _authoringDialogOpen) return;
        RememberPaneFocus();
        _conditionEditor?.CancelTest();
        _detailsPane = details;
        ResizeWorkspace();
        _navigatingView = true;
        try
        {
            if (details) FocusDetail();
            else
            {
                if (Selected is { } action) ActionsList.ScrollIntoView(action);
                if (_sequenceFocus is null || !Within(_sequenceFocus, ActionsList) || !_sequenceFocus.Focus(FocusState.Keyboard))
                {
                    if (Selected is { } selected && ActionsList.ContainerFromItem(selected) is Control row) row.Focus(FocusState.Keyboard);
                    else ActionsList.Focus(FocusState.Keyboard);
                }
            }
        }
        finally { _navigatingView = false; }
    }

    private void FocusDetail()
    {
        _navigatingView = true;
        try
        {
            var remembered = _rawOpen ? _rawFocus : _actionFocus;
            var root = _rawOpen ? (DependencyObject)RawContent : InspectorScroller;
            if (remembered is not null && Within(remembered, root) && remembered.IsEnabled
                && remembered.Visibility == Visibility.Visible && remembered.Focus(FocusState.Keyboard)) return;
            Control target = _rawOpen ? BackToAction : RawToggle.IsEnabled ? RawToggle : InspectorScroller;
            target.Focus(FocusState.Keyboard);
        }
        finally { _navigatingView = false; }
    }

    private bool HandlePaneKey(VirtualKey key, bool control, bool alt, bool windows)
    {
        // F6 is local to the editor and never consumes a shell chord or typed character.
        if (key != VirtualKey.F6 || control || alt || windows || IsPreviewMode
            || _modalConditionEditor is not null || _authoringDialogOpen) return false;
        RememberPaneFocus(); ShowPane(!_detailsPane); return true;
    }
    private void Editor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || e.Key != VirtualKey.F6) return;
        static bool Down(VirtualKey key) => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        e.Handled = HandlePaneKey(e.Key, Down(VirtualKey.Control), Down(VirtualKey.Menu), Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows));
    }
}
