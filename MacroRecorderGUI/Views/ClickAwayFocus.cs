using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace MacroRecorderGUI.Views;

/// <summary>Only background presses release text focus. Controls own their gestures and tab order.</summary>
internal static class ClickAwayFocus
{
    public static void Attach(FrameworkElement root, Control focusTarget)
    {
        // WinUI requires IsTabStop even for programmatic focus. Enable the surface only
        // while it owns focus, so keyboard navigation between actual controls is unchanged.
        focusTarget.IsTabStop = false;
        focusTarget.LostFocus += (_, _) =>
        {
            if (focusTarget.FocusState == FocusState.Unfocused) focusTarget.IsTabStop = false;
        };
        root.PointerPressed += (_, e) =>
        {
            if (IsBackgroundPress(root, e.OriginalSource as DependencyObject, e.GetCurrentPoint(root).Properties.IsLeftButtonPressed, e.Handled))
                LeaveTextInput(root, focusTarget);
        };
    }

    internal static bool IsBackgroundPress(FrameworkElement root, DependencyObject? source, bool primary, bool handled)
        => IsBackgroundPress(root, Ancestors(source), primary, handled);

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject? source)
    {
        for (; source is not null; source = VisualTreeHelper.GetParent(source)) yield return source;
    }

    internal static bool IsBackgroundPress(FrameworkElement root, IEnumerable<DependencyObject> route, bool primary, bool handled)
    {
        if (!primary || handled) return false;
        foreach (var source in route)
        {
            if (ReferenceEquals(source, root)) return true;
            // Scrollbars own pointer gestures even though they are not keyboard tab stops.
            if (IsTextInput(source) || source is ScrollBar or TextBlock { IsTextSelectionEnabled: true } or RichTextBlock { IsTextSelectionEnabled: true }
                || source is Control { IsTabStop: true } and not UserControl and not ScrollViewer) return false;
        }
        return false; // Popups and other XamlRoots own their own focus scopes.
    }

    public static void LeaveTextInput(FrameworkElement root, Control focusTarget)
    {
        if (root.XamlRoot is null) return;
        var focused = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
        if (!IsTextInput(focused)) return;
        for (var ancestor = focused; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ReferenceEquals(ancestor, root))
            {
                focusTarget.IsTabStop = true;
                focusTarget.Focus(FocusState.Programmatic);
                if (focusTarget.FocusState == FocusState.Unfocused) focusTarget.IsTabStop = false;
                return;
            }
    }

    public static bool IsTextInput(DependencyObject? source)
    {
        for (; source is not null; source = VisualTreeHelper.GetParent(source))
            if (source is TextBox or NumberBox or RichEditBox or PasswordBox) return true;
        return false;
    }
}
