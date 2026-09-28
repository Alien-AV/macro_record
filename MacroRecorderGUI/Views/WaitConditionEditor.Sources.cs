using System.Globalization;
using MacroRecorderGUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ProtobufGenerated;

namespace MacroRecorderGUI.Views;

internal sealed partial class WaitConditionEditor
{
    private readonly StackPanel _accessibility = new() { Spacing = 8 }, _ocr = new() { Spacing = 8 }, _memory = new() { Spacing = 8 };
    private readonly List<TextBox> _sourceTextFields = [];
    private readonly List<Button> _sourceButtons = [];
    private readonly StackPanel _ancestors = new() { Spacing = 8 };
    private readonly List<(AccessibilitySelector Template, TextBox Id, TextBox Type, StackPanel Row)> _ancestorFields = [];
    private AccessibilityTextCondition _accessibilityTemplate = null!;
    private OcrTextCondition _ocrTemplate = null!;
    private MemoryCondition _memoryTemplate = null!;
    private Style? _sourceFieldStyle, _sourceButtonStyle;
    internal TextBox ElementId = null!, ElementType = null!, RegionX = null!, RegionY = null!, RegionWidth = null!, RegionHeight = null!, RegionDpi = null!, OcrLanguage = null!;
    internal TextBox MemoryExecutable = null!, MemoryAddress = null!, MemoryModulePath = null!, MemoryModuleVersion = null!, MemoryOffsets = null!, MemoryExpected = null!, MemoryTolerance = null!;
    internal TextBox OcrExecutable = null!, OcrTessdata = null!;
    internal ComboBox AccessibilitySource = null!, RegionCoordinates = null!, MemoryAddressMode = null!, MemoryType = null!, MemoryComparison = null!;
    internal CheckBox MemoryOptIn = null!;
    internal WaitTextPredicateFields AccessibilityPredicate = null!, OcrPredicate = null!;
    private StackPanel _moduleFields = null!;
    private TextBlock _memoryPolicy = null!, _memoryComparisonNote = null!;
    private WaitSourcePreferences _sourcePreferences = WaitSourcePreferences.Current;
    private bool _additionalSourcesCreated;
    private int _previousSource;
    private CapturedWaitPixel? _regionFirstCorner;
    private readonly TextBlock _regionSelection = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };

    partial void InitializeAdditionalSources(WaitCondition condition)
    {
        _accessibilityTemplate = condition.AccessibilityText?.Clone() ?? new() { Element = new(), Predicate = new() };
        _ocrTemplate = condition.OcrText?.Clone() ?? new() { Region = new() { Width = 100, Height = 40, ReferenceDpi = 96 }, Language = "eng", Predicate = new() };
        _memoryTemplate = condition.Memory?.Clone() ?? new() { AbsoluteAddress = 0, Expected = "0" };
        Source.ItemsSource = new[] { "A window matches", "A pixel matches", "Accessibility text matches", "Text in a screen region matches (OCR)", "A memory value matches (advanced)" };
        var selected = condition.ConditionCase switch
        {
            WaitCondition.ConditionOneofCase.Window => 0, WaitCondition.ConditionOneofCase.Pixel => 1,
            WaitCondition.ConditionOneofCase.AccessibilityText => 2, WaitCondition.ConditionOneofCase.OcrText => 3,
            WaitCondition.ConditionOneofCase.Memory => 4, _ => -1
        };
        if (selected >= 2) EnsureAdditionalSourceFields();
        Source.SelectedIndex = _previousSource = selected;
        Trigger.SelectedIndex = Enum.IsDefined(condition.Trigger) && (int)condition.Trigger < Trigger.Items.Count ? (int)condition.Trigger : -1;
        Source.SelectionChanged += (_, _) =>
        {
            if (_populating || Source.SelectedIndex == _previousSource) return;
            if (Source.SelectedIndex >= 2) EnsureAdditionalSourceFields();
            var previousMinimum = MinimumPollMilliseconds(_previousSource);
            _previousSource = Source.SelectedIndex;
            var minimum = MinimumPollMilliseconds(Source.SelectedIndex);
            if (decimal.TryParse(Poll.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var poll)
                && poll >= previousMinimum && poll < minimum && poll * 1000 == decimal.Truncate(poll * 1000))
                Poll.Text = minimum.ToString(CultureInfo.InvariantCulture);
            UpdateFields(); UpdateSentence();
        };
    }

    private static decimal MinimumPollMilliseconds(int source) => source switch { 2 => 250m, 3 => 500m, 4 => 100m, _ => 10m };

    private void EnsureAdditionalSourceFields()
    {
        if (_additionalSourcesCreated) return;
        _additionalSourcesCreated = true;
        var populating = _populating; _populating = true;
        try
        {
            CreateAccessibilityFields(); CreateOcrFields(); CreateMemoryFields();
            var position = Children.IndexOf(_pixel) + 1;
            Children.Insert(position, _accessibility); Children.Insert(position + 1, _ocr); Children.Insert(position + 2, _memory);
            if (_sourceFieldStyle is not null && _sourceButtonStyle is not null) ApplyStyles(_sourceFieldStyle, _sourceButtonStyle);
        }
        finally { _populating = populating; }
    }

    private void CreateAccessibilityFields()
    {
        _accessibility.Children.Add(SourceNote("Choose a window above, then choose an accessible element. Identification fields stay separate from the text being tested."));
        CreateAccessibilityChoiceControls();
        ElementId = SourceField("Element automation ID", _accessibilityTemplate.Element?.AutomationId ?? "");
        ElementType = SourceField("Element control type ID (0 = any)", (_accessibilityTemplate.Element?.ControlType ?? 0).ToString(CultureInfo.InvariantCulture));
        AccessibilitySource = Choice("Read text from", ["Accessible name", "Document text (Text pattern)", "Control value (Value pattern)"], (int)_accessibilityTemplate.Source);
        AccessibilityPredicate = new(_accessibilityTemplate.Predicate); AccessibilityPredicate.Changed += Change;
        _accessibility.Children.Add(ElementId); _accessibility.Children.Add(ElementType); _accessibility.Children.Add(AccessibilitySource);
        _accessibility.Children.Add(AccessibilityPredicate);
        foreach (var ancestor in _accessibilityTemplate.Ancestors) AddAncestor(ancestor);
        var ancestorPanel = new StackPanel { Spacing = 8 };
        ancestorPanel.Children.Add(SourceNote("Exact parent chain, nearest parent first. Leave empty when the element is uniquely identified within the window. Maximum 16 parents."));
        ancestorPanel.Children.Add(_ancestors);
        ancestorPanel.Children.Add(SourceButton("Add parent selector", () =>
        {
            if (_ancestorFields.Count == 16) { Feedback.Text = "At most 16 parent selectors are supported."; return; }
            AddAncestor(new()); Change();
        }));
        _accessibility.Children.Add(SourceExpander("Advanced element ancestry", ancestorPanel));
    }

    private void AddAncestor(AccessibilitySelector template)
    {
        var row = new StackPanel { Spacing = 6 };
        var id = SourceField("Parent automation ID", template.AutomationId);
        var type = SourceField("Parent control type ID (0 = any)", template.ControlType.ToString(CultureInfo.InvariantCulture));
        row.Children.Add(id); row.Children.Add(type);
        row.Children.Add(SourceButton("Remove this parent", () =>
        {
            RemoveAncestor(row); Change();
        }));
        _ancestorFields.Add((template.Clone(), id, type, row)); _ancestors.Children.Add(row);
    }

    private void RemoveAncestor(StackPanel row)
    {
        foreach (var field in row.Children.OfType<TextBox>()) _sourceTextFields.Remove(field);
        foreach (var button in row.Children.OfType<Button>()) _sourceButtons.Remove(button);
        _ancestorFields.RemoveAll(value => ReferenceEquals(value.Row, row)); _ancestors.Children.Remove(row);
    }

    private void CreateOcrFields()
    {
        _ocr.Children.Add(SourceNote("OCR reads visible screen pixels using local Tesseract 5. Choose the region and an installed language; covered or minimized client regions are unavailable."));
        RegionCoordinates = Choice("Region coordinates", ["Desktop physical pixels", "Window client physical pixels", "Window client logical offsets"], (int)(_ocrTemplate.Region?.Coordinates ?? PixelCoordinates.DesktopPhysical));
        RegionCoordinates.SelectionChanged += (_, _) => UpdateFields();
        _ocr.Children.Add(RegionCoordinates);
        var region = _ocrTemplate.Region;
        RegionX = SourceField("Region left", (region?.X ?? 0).ToString(CultureInfo.InvariantCulture));
        RegionY = SourceField("Region top", (region?.Y ?? 0).ToString(CultureInfo.InvariantCulture));
        RegionWidth = SourceField("Region width", (region?.Width ?? 100).ToString(CultureInfo.InvariantCulture));
        RegionHeight = SourceField("Region height", (region?.Height ?? 40).ToString(CultureInfo.InvariantCulture));
        RegionDpi = SourceField("Region reference DPI", (region?.ReferenceDpi ?? 96).ToString(CultureInfo.InvariantCulture));
        _ocr.Children.Add(CreatePickerButton("Pick first region corner · 3 seconds", WaitCaptureTarget.PointerPixel, ApplyFirstRegionCorner));
        _ocr.Children.Add(CreatePickerButton("Pick opposite region corner · 3 seconds", WaitCaptureTarget.PointerPixel, ApplyRegionCorner));
        _ocr.Children.Add(_regionSelection);
        foreach (var control in new Control[] { RegionX, RegionY, RegionWidth, RegionHeight, RegionDpi }) _ocr.Children.Add(control);
        OcrLanguage = SourceField("OCR language identifier", _ocrTemplate.Language);
        _ocr.Children.Add(OcrLanguage);
        CreateLanguageChoiceControls();
        OcrPredicate = new(_ocrTemplate.Predicate); OcrPredicate.Changed += Change; _ocr.Children.Add(OcrPredicate);
        var local = new StackPanel { Spacing = 8 };
        OcrExecutable = LocalSourceField("Local Tesseract 5 executable path", _sourcePreferences.Options.TesseractExecutablePath);
        OcrTessdata = LocalSourceField("Local tessdata directory", _sourcePreferences.Options.TessdataDirectory);
        // Local configuration is deliberately independent of a condition draft.
        local.Children.Add(SourceNote("These installation paths are saved only on this computer. Macros cannot choose an executable or install languages."));
        local.Children.Add(OcrExecutable); local.Children.Add(OcrTessdata);
        local.Children.Add(SourceAsyncButton("Save local OCR installation", SaveOcrSettingsAsync));
        _ocr.Children.Add(SourceExpander("Local OCR installation", local));
    }

    internal void ApplyFirstRegionCorner(WaitTargetCapture capture)
    {
        if (_disposed || capture.Pixel is not { } first) return;
        _regionFirstCorner = first;
        _regionSelection.Text = $"First corner: ({first.X}, {first.Y}). Pick the opposite corner to complete the region.";
    }

    internal void ApplyRegionCorner(WaitTargetCapture capture)
    {
        if (_disposed || capture.Pixel is not { } last) return;
        if (_regionFirstCorner is not { } first) { _regionSelection.Text = "Pick the first region corner before the opposite corner."; return; }
        var width = Math.Abs((long)last.X - first.X) + 1;
        var height = Math.Abs((long)last.Y - first.Y) + 1;
        if (width > 4096 || height > 4096 || width * height > 1_000_000)
        { _regionSelection.Text = "Choose a smaller region: at most 4096 pixels per side and one million pixels total."; return; }
        _populating = true;
        try
        {
            RegionCoordinates.SelectedIndex = (int)PixelCoordinates.DesktopPhysical;
            RegionX.Text = Math.Min(first.X, last.X).ToString(CultureInfo.InvariantCulture);
            RegionY.Text = Math.Min(first.Y, last.Y).ToString(CultureInfo.InvariantCulture);
            RegionWidth.Text = width.ToString(CultureInfo.InvariantCulture); RegionHeight.Text = height.ToString(CultureInfo.InvariantCulture);
        }
        finally { _populating = false; }
        _regionFirstCorner = null; _regionSelection.Text = $"Desktop region selected: {width} × {height} pixels. Review the coordinates below.";
        UpdateFields(); Change();
    }

    private void CreateMemoryFields()
    {
        _memory.Children.Add(SourceNote("Read one typed value from a specific application. Pointer chains dereference each address and then add the next offset. Values are never written."));
        MemoryOptIn = new() { Content = "Allow read-only memory waits on this computer", IsChecked = _sourcePreferences.Options.MemoryEnabled };
        AutomationProperties.SetName(MemoryOptIn, "Allow read-only memory waits on this computer");
        _memoryPolicy = SourceNote(_sourcePreferences.Warning ?? "This permission is saved locally. Importing or editing a macro cannot enable it.");
        MemoryOptIn.Click += async (_, _) => await SaveMemoryPolicyAsync();
        _memory.Children.Add(MemoryOptIn); _memory.Children.Add(_memoryPolicy);
        _memory.Children.Add(CreateWindowPickerButton("Use hovered window's application · 3 seconds", target => MemoryExecutable.Text = target.ExecutablePath));
        MemoryExecutable = SourceField("Target executable full path", _memoryTemplate.ExecutablePath); _memory.Children.Add(MemoryExecutable);
        CreateProcessChoiceControls();
        MemoryType = Choice("Value type", ["Unsigned 8-bit integer", "Signed 8-bit integer", "Unsigned 16-bit integer", "Signed 16-bit integer", "Unsigned 32-bit integer", "Signed 32-bit integer", "Unsigned 64-bit integer", "Signed 64-bit integer", "32-bit floating point", "64-bit floating point"], (int)_memoryTemplate.ScalarType);
        MemoryComparison = Choice("Numeric comparison", ["Equals", "Does not equal", "Less than", "Less than or equal", "Greater than", "Greater than or equal"], (int)_memoryTemplate.Comparison);
        MemoryExpected = SourceField("Expected number (decimal)", _memoryTemplate.Expected);
        MemoryTolerance = SourceField("Floating-point equality tolerance", _memoryTemplate.Tolerance.ToString("R", CultureInfo.InvariantCulture));
        _memoryComparisonNote = SourceNote("Changes compares with the first valid runtime value. The saved comparison and expected number are not used by this trigger, but must remain valid so they can be preserved when switching triggers.");
        foreach (var control in new UIElement[] { MemoryType, _memoryComparisonNote, MemoryComparison, MemoryExpected, MemoryTolerance }) _memory.Children.Add(control);
        MemoryType.SelectionChanged += (_, _) => UpdateFields(); MemoryComparison.SelectionChanged += (_, _) => UpdateFields();
        var address = new StackPanel { Spacing = 8 };
        MemoryAddressMode = Choice("Starting address", ["Absolute address", "Module base plus offset"], _memoryTemplate.AddressCase == MemoryCondition.AddressOneofCase.Module ? 1 : 0);
        MemoryAddressMode.SelectionChanged += (_, _) => UpdateFields();
        MemoryAddress = SourceField("Base address or module offset (decimal or 0x hex)", WaitSourceInput.FormatAddress(_memoryTemplate.Module?.Offset ?? _memoryTemplate.AbsoluteAddress));
        MemoryModulePath = SourceField("Module full path", _memoryTemplate.Module?.Path ?? "");
        MemoryModuleVersion = SourceField("Exact module file version", _memoryTemplate.Module?.FileVersion ?? "");
        MemoryOffsets = SourceField("Pointer offsets, in order (comma separated)", string.Join(", ", _memoryTemplate.PointerOffsets.Select(WaitSourceInput.FormatOffset)));
        _moduleFields = new() { Spacing = 8 }; _moduleFields.Children.Add(MemoryModulePath); _moduleFields.Children.Add(MemoryModuleVersion);
        CreateModuleChoiceControls(_moduleFields);
        address.Children.Add(MemoryAddressMode); address.Children.Add(_moduleFields); address.Children.Add(MemoryAddress); address.Children.Add(MemoryOffsets);
        address.Children.Add(SourceNote("Absolute addresses can change after restart. Module addressing checks the exact path and file version. Leave offsets empty for a direct scalar read; at most 16 offsets. Example: 0x20, -0x10."));
        _memory.Children.Add(SourceExpander("Address and pointer chain", address, expanded: true));
    }

    partial void ReadAdditionalSource(WaitCondition result, ref bool handled)
    {
        var source = Source.SelectedIndex;
        if (source < 0) throw new ArgumentException("Choose a supported condition source.");
        var original = _template.ConditionCase switch { WaitCondition.ConditionOneofCase.Window => 0, WaitCondition.ConditionOneofCase.Pixel => 1,
            WaitCondition.ConditionOneofCase.AccessibilityText => 2, WaitCondition.ConditionOneofCase.OcrText => 3, WaitCondition.ConditionOneofCase.Memory => 4, _ => -1 };
        // A deliberate source conversion selects that source's semantics. An
        // unsupported imported version is never repaired by ordinary field edits.
        if (source != original && _template.SemanticsVersion is 1 or 2) result.SemanticsVersion = source >= 2 ? 2u : 1u;
        if (source < 2) return;
        EnsureAdditionalSourceFields();
        handled = true;
        if (source == 2)
        {
            var value = _accessibilityTemplate.Clone(); value.Target = ReadSourceTarget(value.Target);
            value.Element ??= new(); value.Element.AutomationId = ElementId.Text; value.Element.ControlType = SourceUInt(ElementType, "Element control type");
            value.Ancestors.Clear();
            foreach (var fields in _ancestorFields)
            {
                var ancestor = fields.Template.Clone(); ancestor.AutomationId = fields.Id.Text; ancestor.ControlType = SourceUInt(fields.Type, "Parent control type"); value.Ancestors.Add(ancestor);
            }
            value.Source = (AccessibilityTextSource)AccessibilitySource.SelectedIndex; value.Predicate = AccessibilityPredicate.Read(); result.AccessibilityText = value;
        }
        else if (source == 3)
        {
            var value = _ocrTemplate.Clone(); value.Region ??= new(); var region = value.Region;
            region.Coordinates = (PixelCoordinates)RegionCoordinates.SelectedIndex;
            region.Target = region.Coordinates == PixelCoordinates.DesktopPhysical ? null : ReadSourceTarget(region.Target);
            region.X = SourceInt(RegionX, "Region left"); region.Y = SourceInt(RegionY, "Region top");
            region.Width = SourceUInt(RegionWidth, "Region width"); region.Height = SourceUInt(RegionHeight, "Region height");
            if (region.Coordinates == PixelCoordinates.ClientLogical) region.ReferenceDpi = SourceUInt(RegionDpi, "Region reference DPI");
            value.Language = OcrLanguage.Text; value.Predicate = OcrPredicate.Read(); result.OcrText = value;
        }
        else if (source == 4)
        {
            var value = _memoryTemplate.Clone(); value.ExecutablePath = MemoryExecutable.Text;
            var address = WaitSourceInput.Address(MemoryAddress.Text, MemoryAddressMode.SelectedIndex == 1 ? "Module offset" : "Base address");
            if (MemoryAddressMode.SelectedIndex == 0) value.AbsoluteAddress = address;
            else if (MemoryAddressMode.SelectedIndex == 1)
            {
                var module = value.Module?.Clone() ?? new(); module.Path = MemoryModulePath.Text; module.FileVersion = MemoryModuleVersion.Text; module.Offset = address; value.Module = module;
            }
            else throw new ArgumentException("Choose an absolute or module-relative starting address.");
            value.PointerOffsets.Clear(); value.PointerOffsets.Add(WaitSourceInput.Offsets(MemoryOffsets.Text, 16));
            value.ScalarType = (MemoryScalarType)MemoryType.SelectedIndex; value.Comparison = (NumericComparison)MemoryComparison.SelectedIndex;
            value.Expected = MemoryExpected.Text;
            if (!double.TryParse(string.IsNullOrWhiteSpace(MemoryTolerance.Text) ? "0" : MemoryTolerance.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var tolerance) || !double.IsFinite(tolerance) || tolerance < 0)
                throw new ArgumentException("Floating-point tolerance: enter a finite, nonnegative number.");
            value.Tolerance = tolerance; result.Memory = value;
        }
        else throw new ArgumentException("Choose a supported condition source.");
    }

    private WindowSelector ReadSourceTarget(WindowSelector? template)
    {
        var target = (template ?? _template.Window?.Target ?? _template.Pixel?.Target ?? _template.AccessibilityText?.Target ?? _template.OcrText?.Region?.Target)?.Clone() ?? new();
        target.ExecutablePath = Executable.Text; target.WindowClass = Class.Text; target.Title = Title.Text;
        target.TitleMatch = (TitleMatch)TitleRule.SelectedIndex; target.IgnoreTitleCase = IgnoreCase.IsChecked == true; target.AnyMatch = Any.IsChecked == true; return target;
    }

    partial void UpdateAdditionalSourceFields()
    {
        if (RegionCoordinates is null) return;
        var source = Source.SelectedIndex;
        _accessibility.Visibility = source == 2 ? Visibility.Visible : Visibility.Collapsed;
        _ocr.Visibility = source == 3 ? Visibility.Visible : Visibility.Collapsed;
        _memory.Visibility = source == 4 ? Visibility.Visible : Visibility.Collapsed;
        if (source >= 2)
        {
            _target.Visibility = source == 2 || source == 3 && RegionCoordinates.SelectedIndex != 0 ? Visibility.Visible : Visibility.Collapsed;
            Any.Visibility = Visibility.Collapsed;
            if (!_populating && source != _previousSource) Any.IsChecked = false;
            _help.Text = source switch
            {
                2 => "Accessibility lookup stays within one window. Unsupported text access and incomplete reads are reported; they cannot satisfy a negative condition. Minimum polling: 250 ms.",
                3 => "OCR uses the selected language only. Use List installed languages to check Tesseract and its language data. Minimum polling: 500 ms.",
                _ => "Memory access requires local permission. Playback binds one process instance; a process exit stops the wait. Minimum polling: 100 ms."
            };
        }
        var changes = Trigger.SelectedIndex == (int)WaitTrigger.Changes;
        AccessibilityPredicate.SetChangesMode(changes); OcrPredicate.SetChangesMode(changes);
        _memoryComparisonNote.Visibility = changes ? Visibility.Visible : Visibility.Collapsed;
        var expectedLabel = changes ? "Saved expected number (decimal; not used for Changes)" : "Expected number (decimal)";
        var comparisonLabel = changes ? "Saved comparison (not used for Changes)" : "Numeric comparison";
        MemoryExpected.Header = expectedLabel; MemoryComparison.Header = comparisonLabel;
        AutomationProperties.SetName(MemoryExpected, expectedLabel); AutomationProperties.SetName(MemoryComparison, comparisonLabel);
        // Preserve invalid values visibly so imports and unfinished edits can be repaired.
        MemoryTolerance.Visibility = MemoryType.SelectedIndex >= 8 || MemoryTolerance.Text != "0" ? Visibility.Visible : Visibility.Collapsed;
        _moduleFields.Visibility = MemoryAddressMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        RegionDpi.Visibility = RegionCoordinates.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void UseSourcePreferences(WaitSourcePreferences preferences)
    {
        _sourcePreferences = preferences;
        if (!_additionalSourcesCreated) return;
        MemoryOptIn.IsChecked = preferences.Options.MemoryEnabled;
        OcrExecutable.Text = preferences.Options.TesseractExecutablePath; OcrTessdata.Text = preferences.Options.TessdataDirectory;
        _memoryPolicy.Text = preferences.Warning ?? "Permission and OCR installation settings are saved only on this computer.";
        UpdateMemoryQueryAvailability();
    }

    internal async Task SaveMemoryPolicyAsync()
    {
        var enabled = MemoryOptIn.IsChecked == true; MemoryOptIn.IsEnabled = false;
        _chooseProcess.IsEnabled = _chooseModule.IsEnabled = false;
        CancelTest();
        try
        {
            await _sourcePreferences.SetMemoryEnabledAsync(enabled);
            if (!_disposed) _memoryPolicy.Text = enabled ? "Read-only memory waits are enabled on this computer. No permission was added to this macro." : "Read-only memory waits are disabled on this computer.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { if (!_disposed) _memoryPolicy.Text = "Local permission was not saved. " + error.Message; }
        finally { MemoryOptIn.IsChecked = _sourcePreferences.Options.MemoryEnabled; MemoryOptIn.IsEnabled = true; UpdateMemoryQueryAvailability(); }
    }

    internal async Task SaveOcrSettingsAsync()
    {
        CancelTest();
        try
        {
            await _sourcePreferences.SetOcrInstallationAsync(OcrExecutable.Text, OcrTessdata.Text);
            if (!_disposed) Feedback.Text = "Local OCR installation saved. Choose List installed languages to check the installation.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { if (!_disposed) Feedback.Text = "Local OCR installation was not saved. " + error.Message; }
    }

    partial void ApplyAdditionalSourceStyles(Style field, Style button)
    {
        _sourceFieldStyle = field; _sourceButtonStyle = button;
        foreach (var text in _sourceTextFields) text.Style = field;
        foreach (var item in _sourceButtons) item.Style = button;
        AccessibilityPredicate?.ApplyStyles(field); OcrPredicate?.ApplyStyles(field);
    }

    partial void NormalizeAdditionalSourceFields()
    {
        if (Source.SelectedIndex == 4 && string.IsNullOrWhiteSpace(MemoryTolerance.Text)) MemoryTolerance.Text = "0";
    }

    partial void ValidateAdditionalSourceTest()
    {
        if (Source.SelectedIndex == 4 && !_sourcePreferences.Options.MemoryEnabled)
            throw new ArgumentException("Enable read-only memory waits on this computer before testing a memory condition.");
        if (Source.SelectedIndex == 3 && HasUnsavedOcrInstallation())
            throw new ArgumentException("Save the local OCR installation fields before testing or listing languages.");
    }

    private bool HasUnsavedOcrInstallation() => OcrExecutable.Text.Trim() != _sourcePreferences.Options.TesseractExecutablePath
        || OcrTessdata.Text.Trim() != _sourcePreferences.Options.TessdataDirectory;

    private TextBox SourceField(string name, string value)
    {
        var control = Field(name, value); if (_sourceFieldStyle is not null) control.Style = _sourceFieldStyle;
        _sourceTextFields.Add(control); return control;
    }
    private Button SourceButton(string label, Action action)
    {
        var button = new Button { Content = label }; AutomationProperties.SetName(button, label);
        if (_sourceButtonStyle is not null) button.Style = _sourceButtonStyle;
        _sourceButtons.Add(button); button.Click += (_, _) => action(); return button;
    }
    private Button SourceAsyncButton(string label, Func<Task> action)
    {
        var button = new Button { Content = label }; AutomationProperties.SetName(button, label);
        if (_sourceButtonStyle is not null) button.Style = _sourceButtonStyle;
        _sourceButtons.Add(button);
        button.Click += async (_, _) => await action(); return button;
    }
    private TextBox LocalSourceField(string name, string value)
    {
        var field = new TextBox { Header = name, Text = value, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(field, name); _sourceTextFields.Add(field);
        field.TextChanging += (_, _) =>
        {
            CancelAdditionalSourceWork();
            if (LanguageChoices is not null) LanguageChoices.Visibility = Visibility.Collapsed;
        };
        return field;
    }
    private static TextBlock SourceNote(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private static Expander SourceExpander(string label, UIElement content, bool expanded = false) => new()
    { Header = label, Content = content, IsExpanded = expanded, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private static uint SourceUInt(TextBox field, string label) => uint.TryParse(field.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        ? value : throw new ArgumentException($"{label}: enter a whole number from 0 to {uint.MaxValue}.");
    private static int SourceInt(TextBox field, string label) => int.TryParse(field.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
        ? value : throw new ArgumentException($"{label}: enter a signed whole number.");
}
