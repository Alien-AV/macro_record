using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ProtobufGenerated;

namespace MacroRecorderGUI.Views;

/// <summary>Text matching drafts shared by accessibility and OCR authoring.</summary>
internal sealed class WaitTextPredicateFields : StackPanel
{
    private readonly TextPredicate _template;
    internal readonly TextBox Expected;
    internal readonly ComboBox Comparison, Whitespace;
    internal readonly CheckBox IgnoreCase;
    private readonly TextBlock _savedPredicateNote = new()
    {
        Text = "Changes compares with the first valid runtime text. The saved comparison and expected text are not used by this trigger, but must remain valid so they can be preserved when switching triggers.",
        TextWrapping = TextWrapping.Wrap, FontSize = 13, Visibility = Visibility.Collapsed
    };
    public event Action? Changed;

    public WaitTextPredicateFields(TextPredicate? predicate)
    {
        _template = predicate?.Clone() ?? new();
        Spacing = 8;
        Expected = new() { Header = "Expected text", Text = _template.Expected, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, MinHeight = 64, HorizontalAlignment = HorizontalAlignment.Stretch };
        Comparison = new() { Header = "Text comparison", ItemsSource = new[] { "Equals", "Contains", "Does not equal", "Does not contain" },
            SelectedIndex = Enum.IsDefined(_template.Comparison) ? (int)_template.Comparison : -1,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        Whitespace = new() { Header = "Whitespace", ItemsSource = new[] { "Preserve spaces and line breaks", "Collapse whitespace to a single space" },
            SelectedIndex = Enum.IsDefined(_template.Whitespace) ? (int)_template.Whitespace : -1,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        IgnoreCase = new() { Content = "Ignore text case", IsChecked = _template.IgnoreCase };
        foreach (var field in new UIElement[] { _savedPredicateNote, Comparison, Expected, Whitespace, IgnoreCase }) Children.Add(field);
        AutomationProperties.SetName(Expected, "Expected text");
        AutomationProperties.SetName(Comparison, "Text comparison");
        AutomationProperties.SetName(Whitespace, "Whitespace");
        Expected.TextChanging += (_, _) => Changed?.Invoke();
        Comparison.SelectionChanged += (_, _) => Changed?.Invoke();
        Whitespace.SelectionChanged += (_, _) => Changed?.Invoke();
        IgnoreCase.Checked += (_, _) => Changed?.Invoke();
        IgnoreCase.Unchecked += (_, _) => Changed?.Invoke();
    }

    public TextPredicate Read()
    {
        var result = _template.Clone();
        result.Expected = Expected.Text;
        result.Comparison = (TextComparison)Comparison.SelectedIndex;
        result.Whitespace = (TextWhitespace)Whitespace.SelectedIndex;
        result.IgnoreCase = IgnoreCase.IsChecked == true;
        return result;
    }

    public void SetChangesMode(bool changes)
    {
        _savedPredicateNote.Visibility = changes ? Visibility.Visible : Visibility.Collapsed;
        var expectedLabel = changes ? "Saved expected text (not used for Changes)" : "Expected text";
        var comparisonLabel = changes ? "Saved text comparison (not used for Changes)" : "Text comparison";
        Expected.Header = expectedLabel; Comparison.Header = comparisonLabel;
        AutomationProperties.SetName(Expected, expectedLabel); AutomationProperties.SetName(Comparison, comparisonLabel);
    }

    public void ApplyStyles(Style field) => Expected.Style = field;
}
