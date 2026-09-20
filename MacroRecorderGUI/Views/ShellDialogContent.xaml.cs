using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MacroRecorderGUI.Views;

public sealed partial class ShellDialogContent : UserControl
{
    public ShellDialogContent()
    {
        InitializeComponent();
        IsTabStop = false;
        ClickAwayFocus.Attach(this, this);
    }

    internal StackPanel Fields => FieldsPanel;
    internal string Title => Heading.Text;
    internal void SetHeading(string title, string glyph, string description)
    {
        Heading.Text = title;
        DialogIcon.Glyph = glyph;
        Description.Text = description;
    }

    internal void SetSafety(string shortcut, string note = "")
    {
        SafetyShortcut.Text = shortcut;
        SafetyNote.Text = note;
        SafetyNote.Visibility = string.IsNullOrEmpty(note) ? Visibility.Collapsed : Visibility.Visible;
        SafetyPanel.Visibility = Footer.Visibility = Visibility.Visible;
    }

    internal void ShowError(string message)
    {
        ValidationError.Text = message;
        ValidationError.Visibility = Footer.Visibility = Visibility.Visible;
    }

    internal void FitToViewport(XamlRoot root)
    {
        // Leave room for native dialog padding, its command row and the popup margin.
        // The star row gives up space first, keeping the safety footer outside the scroll area.
        Width = Math.Max(0, Math.Min(488, root.Size.Width - 96));
        MaxHeight = Math.Max(0, Math.Min(600, root.Size.Height - 160));
    }
}
