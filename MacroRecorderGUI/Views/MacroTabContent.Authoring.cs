using System.Globalization;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MacroRecorderGUI.Views;

internal enum AuthoringKind { Click, Shortcut, PointerMovement }

internal sealed class ActionAuthoringFields : StackPanel
{
    internal readonly AuthoringKind Kind;
    internal readonly ComboBox ButtonChoice, KeyChoice, SpaceChoice;
    internal readonly TextBox X, Y, Pause, Hold;
    internal readonly CheckBox ControlKey, ShiftKey, AltKey, WindowsKey;
    internal readonly TextBlock Feedback = new() { TextWrapping = TextWrapping.Wrap };
    private readonly uint[] _keys;

    internal ActionAuthoringFields(AuthoringKind kind, Style fieldStyle)
    {
        Kind = kind; Spacing = 10;
        Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Text = kind switch
        {
            AuthoringKind.Click => "Click at the pointer's position when this action runs.",
            AuthoringKind.Shortcut => "Press and release a key with the selected modifiers. This does not insert text.",
            _ => "Add one pointer movement. Screen pixels are an absolute destination; device counts are a relative movement."
        } });
        ButtonChoice = Choice("Mouse button", ["Left", "Right", "Middle", "X1", "X2"]);
        _keys = Enumerable.Range(1, 255).Select(value => (uint)value).Where(ActionAuthoring.IsSupportedShortcutKey).ToArray();
        KeyChoice = Choice("Key", _keys.Select(key => $"{ActionProjection.KeyName(key)} ({key})").ToArray());
        KeyChoice.SelectedIndex = Array.IndexOf(_keys, 0x41u);
        SpaceChoice = Choice("Coordinates", ["Virtual desktop · pixels", "Primary screen · pixels", "Relative device counts"]);
        X = Field("X", "0"); Y = Field("Y", "0");
        Pause = Field("Pause before · seconds", "0"); Hold = Field("Hold for · seconds", "0");
        ControlKey = new() { Content = "Ctrl" }; ShiftKey = new() { Content = "Shift" };
        AltKey = new() { Content = "Alt" }; WindowsKey = new() { Content = "Windows" };
        if (kind == AuthoringKind.Click) Children.Add(ButtonChoice);
        if (kind == AuthoringKind.Shortcut)
        {
            Children.Add(KeyChoice);
            foreach (var check in new[] { ControlKey, ShiftKey, AltKey, WindowsKey }) Children.Add(check);
        }
        if (kind == AuthoringKind.PointerMovement)
        { Children.Add(SpaceChoice); Children.Add(X); Children.Add(Y); }
        Children.Add(Pause);
        if (kind != AuthoringKind.PointerMovement)
            Children.Add(new Expander { Header = "Hold timing", Content = Hold, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch });
        Children.Add(new TextBlock { Text = "Times accept up to six decimal places. All recorded inputs must be released at the insertion point.", TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        Children.Add(Feedback);

        TextBox Field(string label, string text)
        {
            var field = new TextBox { Header = label, Text = text, Style = fieldStyle };
            AutomationProperties.SetName(field, label); return field;
        }
        static ComboBox Choice(string label, string[] items)
        {
            var field = new ComboBox { Header = label, ItemsSource = items, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(field, label); return field;
        }
    }

    internal IReadOnlyList<InputEvent> Insert(ActionEditor editor, RecordedAction? after)
    {
        var pause = checked((ulong)TimeText.ParseSeconds(Pause.Text));
        return Kind switch
        {
            AuthoringKind.Click => editor.InsertClick(after, new ClickDefinition(ButtonChoice.SelectedIndex switch
            {
                0 => MouseButton.Left, 1 => MouseButton.Right, 2 => MouseButton.Middle, 3 => MouseButton.X1, 4 => MouseButton.X2,
                _ => throw new ArgumentException("Choose a mouse button.")
            }, pause, checked((ulong)TimeText.ParseSeconds(Hold.Text)))),
            AuthoringKind.Shortcut => editor.InsertShortcut(after, new ShortcutDefinition(
                KeyChoice.SelectedIndex >= 0 && KeyChoice.SelectedIndex < _keys.Length ? _keys[KeyChoice.SelectedIndex] : throw new ArgumentException("Choose a supported key."),
                (ControlKey.IsChecked == true ? ShortcutModifiers.Control : ShortcutModifiers.None)
                | (ShiftKey.IsChecked == true ? ShortcutModifiers.Shift : ShortcutModifiers.None)
                | (AltKey.IsChecked == true ? ShortcutModifiers.Alt : ShortcutModifiers.None)
                | (WindowsKey.IsChecked == true ? ShortcutModifiers.Windows : ShortcutModifiers.None),
                pause, checked((ulong)TimeText.ParseSeconds(Hold.Text)))),
            _ => editor.InsertPointerMovement(after, new PointerMovementDefinition(int.Parse(X.Text, CultureInfo.InvariantCulture),
                int.Parse(Y.Text, CultureInfo.InvariantCulture), SpaceChoice.SelectedIndex switch
                {
                    0 => CoordinateSpace.AbsoluteDesktop, 1 => CoordinateSpace.AbsolutePrimary, 2 => CoordinateSpace.RelativeCounts,
                    _ => throw new ArgumentException("Choose a coordinate frame.")
                }, pause))
        };
    }
}

public sealed partial class MacroTabContent
{
    private bool _authoringDialogOpen;
    private async void AddClick_Click(object sender, RoutedEventArgs e) => await AddActionAsync(AuthoringKind.Click);
    private async void AddShortcut_Click(object sender, RoutedEventArgs e) => await AddActionAsync(AuthoringKind.Shortcut);
    private async void AddMovement_Click(object sender, RoutedEventArgs e) => await AddActionAsync(AuthoringKind.PointerMovement);

    private Action<ActionAuthoringFields>? PrepareActionInsertion()
    {
        if (_editor is null || _macro is null) return null;
        var editor = _editor; var macro = _macro;
        var anchor = ActionsList.SelectedItems.OfType<RecordedAction>().OrderBy(action => action.Start).LastOrDefault()?.First;
        if (!TryCommitPendingEdits()) return null;
        return fields =>
        {
            if (_disposed || !ReferenceEquals(_editor, editor) || !ReferenceEquals(_macro, macro))
                throw new ArgumentException("The recording is no longer open. Reopen Add action.");
            RefreshEditor();
            var action = anchor is null ? null : editor.Projection.ActionAt(macro.Events.IndexOf(anchor))
                ?? throw new ArgumentException("The selected action is no longer available. Reopen Add action.");
            fields.Insert(editor, action);
        };
    }
    private void BeginAuthoringDialog()
    {
        if (_disposed || _authoringDialogOpen || _modalConditionEditor is not null)
            throw new InvalidOperationException("An Add action dialog cannot be opened here.");
        _authoringDialogOpen = true;
    }
    private void EndAuthoringDialog() => _authoringDialogOpen = false;

    private async Task AddActionAsync(AuthoringKind kind)
    {
        var insert = PrepareActionInsertion();
        if (insert is null) return;
        var editor = _editor;
        var fields = new ActionAuthoringFields(kind, (Style)Resources.MergedDictionaries[0]["DesignedEditorField"]);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = kind switch { AuthoringKind.Click => "Add click", AuthoringKind.Shortcut => "Add shortcut", _ => "Add pointer movement" },
            PrimaryButtonText = "Add action", CloseButtonText = "Cancel",
            Content = new ScrollViewer { Content = fields, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { insert(fields); }
            catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
            { fields.Feedback.Text = error.Message; args.Cancel = true; }
        };
        BeginAuthoringDialog();
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { EndAuthoringDialog(); }
        if (!_disposed && ReferenceEquals(_editor, editor))
        {
            RefreshEditor(resetDrafts: result == ContentDialogResult.Primary);
            if (result == ContentDialogResult.Primary) Status = "Action added. Undo is available.";
        }
    }
}
