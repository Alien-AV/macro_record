using System.Globalization;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ProtobufGenerated;

namespace MacroRecorderGUI.Views;

/// <summary>Editable drafts with explicit one-shot picking. Waiting starts only from Test condition.</summary>
internal sealed partial class WaitConditionEditor : StackPanel, IDisposable
{
    private readonly WaitCondition _template;
    private readonly WaitRunner _runner;
    private bool _populating = true, _disposed;
    private CancellationTokenSource? _test;
    private long _testGeneration;
    public bool IsDirty { get; private set; }
    public event Action? Changed;
    internal readonly ComboBox Source, Trigger, WindowRule, Coordinates, TitleRule;
    internal readonly TextBox Executable, Class, Title, X, Y, Rgb, Tolerance, Dpi, Timeout, Stability, Poll;
    internal readonly CheckBox IgnoreCase, Any, NotEqual;
    internal readonly TextBlock Feedback = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly StackPanel _target = new() { Spacing = 8 }, _pixel = new() { Spacing = 8 };
    private readonly Button _testButton = new() { Content = "Test condition · up to 5 s" };
    private readonly TextBlock _help = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly TextBlock _sentence = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14 };

    public WaitConditionEditor(WaitCondition condition, WaitRunner? runner = null, IWaitTargetPicker? picker = null,
        Func<TimeSpan, CancellationToken, Task>? pickerDelay = null)
    {
        _template = condition.Clone();
        _runner = runner ?? WaitRunner.Desktop;
        _picker = picker ?? new WaitTargetPicker(new WindowsWaitTargetCaptureApi());
        _pickerDelay = pickerDelay ?? Task.Delay;
        Spacing = 10;
        Source = Choice("Wait until", ["A window matches", "A pixel matches"], condition.Pixel is null ? 0 : 1);
        Trigger = Choice("Trigger", ["Is true", "Becomes true", "Changes from starting value", "New matching window"], (int)condition.Trigger);
        Children.Add(_sentence); Children.Add(Source);
        WindowRule = Choice("Window condition", ["Exists", "Visible", "Foreground", "Absent"], (int)(condition.Window?.Test ?? WindowTest.Visible));
        Coordinates = Choice("Pixel coordinates", ["Desktop physical pixels", "Window client physical pixels", "Window client logical offsets"], (int)(condition.Pixel?.Coordinates ?? PixelCoordinates.DesktopPhysical));
        Children.Add(WindowRule); Children.Add(Coordinates);
        var target = condition.Window?.Target ?? condition.Pixel?.Target ?? new WindowSelector();
        Executable = Field("Full executable path (optional)", target.ExecutablePath);
        Class = Field("Window class (optional)", target.WindowClass);
        Title = Field("Window title (optional)", target.Title);
        TitleRule = Choice("Title matching", ["Exact", "Contains"], (int)target.TitleMatch);
        IgnoreCase = Check("Ignore title case", target.IgnoreTitleCase);
        Any = Check("Allow any matching window", target.AnyMatch);
        foreach (var control in new UIElement[] { Executable, Class, Title, TitleRule, IgnoreCase, Any }) _target.Children.Add(control);
        Children.Add(_target);
        _target.Children.Insert(0, CreateWindowPickerButton("Pick hovered window · 3 seconds", ApplyWindowCapture));
        var pixel = condition.Pixel;
        X = Field("Pixel X", (pixel?.X ?? 0).ToString(CultureInfo.InvariantCulture));
        Y = Field("Pixel Y", (pixel?.Y ?? 0).ToString(CultureInfo.InvariantCulture));
        Rgb = Field("Expected RGB (six hex digits)", (pixel?.Rgb ?? 0).ToString("X6", CultureInfo.InvariantCulture));
        Tolerance = Field("Maximum channel tolerance (0–255)", (pixel?.Tolerance ?? 0).ToString(CultureInfo.InvariantCulture));
        NotEqual = Check("Color is not equal", pixel?.NotEqual ?? false);
        Dpi = Field("Reference DPI for logical offsets", (pixel?.ReferenceDpi is > 0 ? pixel.ReferenceDpi : 96).ToString(CultureInfo.InvariantCulture));
        foreach (var control in new UIElement[] { X, Y, Rgb, Tolerance, NotEqual }) _pixel.Children.Add(control);
        Children.Add(_pixel);
        _pixel.Children.Insert(0, CreatePickerButton("Pick desktop pixel · 3 seconds", WaitCaptureTarget.PointerPixel, ApplyPixelCapture));
        _pixel.Children.Add(new TextBlock { Text = "Picking a pixel uses its current desktop physical position and RGB. You can edit the captured values afterwards.", TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        Timeout = Field("Timeout · seconds (stop on failure)", (condition.TimeoutUs / 1_000_000m).ToString(CultureInfo.InvariantCulture));
        Stability = Field("Stable for · milliseconds", (condition.StableForUs / 1000m).ToString(CultureInfo.InvariantCulture));
        Poll = Field("Polling interval · milliseconds", (condition.PollIntervalUs / 1000m).ToString(CultureInfo.InvariantCulture));
        var advanced = new StackPanel { Spacing = 8 };
        advanced.Children.Add(Timeout); advanced.Children.Add(Stability);
        advanced.Children.Add(Trigger); advanced.Children.Add(Poll); advanced.Children.Add(Dpi);
        Children.Add(new Expander { Header = "Timeout, stability and advanced options", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch });
        Children.Add(_help); Children.Add(_testButton); Children.Add(Feedback);
        Source.SelectionChanged += (_, _) => UpdateFields(); Coordinates.SelectionChanged += (_, _) => UpdateFields(); Trigger.SelectionChanged += (_, _) => UpdateFields();
        _testButton.Click += async (_, _) => await TestAsync();
        InitializeAdditionalSources(condition);
        Unloaded += (_, _) => CancelTest();
        UpdateFields(); _populating = false; UpdateSentence();
    }

    public WaitCondition Read()
    {
        var result = _template.Clone();
        // Preserve unsupported imported versions: editing a field cannot silently upgrade semantics.
        result.Trigger = (WaitTrigger)Trigger.SelectedIndex;
        result.TimeoutUs = Duration(Timeout.Text, 1_000_000);
        result.StableForUs = Duration(Stability.Text, 1000);
        result.PollIntervalUs = Duration(Poll.Text, 1000);
        var handled = false;
        ReadAdditionalSource(result, ref handled);
        if (handled) { WaitValidation.Validate(result); return result; }
        var target = (result.Window?.Target ?? result.Pixel?.Target)?.Clone() ?? new WindowSelector();
        target.ExecutablePath = Executable.Text; target.WindowClass = Class.Text; target.Title = Title.Text;
        target.TitleMatch = (TitleMatch)TitleRule.SelectedIndex; target.IgnoreTitleCase = IgnoreCase.IsChecked == true; target.AnyMatch = Any.IsChecked == true;
        if (Source.SelectedIndex == 0)
        {
            var window = result.Window?.Clone() ?? new WindowCondition(); window.Target = target; window.Test = (WindowTest)WindowRule.SelectedIndex;
            result.Window = window;
        }
        else if (Source.SelectedIndex == 1)
        {
            var pixel = result.Pixel?.Clone() ?? new PixelCondition();
            pixel.Coordinates = (PixelCoordinates)Coordinates.SelectedIndex;
            pixel.Target = pixel.Coordinates == PixelCoordinates.DesktopPhysical ? null : target;
            pixel.X = int.Parse(X.Text, CultureInfo.InvariantCulture); pixel.Y = int.Parse(Y.Text, CultureInfo.InvariantCulture);
            if (result.Trigger != WaitTrigger.Changes)
            {
                var rgb = Rgb.Text.Trim().TrimStart('#');
                if (rgb.Length != 6) throw new ArgumentException("RGB must contain exactly six hexadecimal digits.");
                pixel.Rgb = uint.Parse(rgb, NumberStyles.HexNumber, CultureInfo.InvariantCulture); pixel.NotEqual = NotEqual.IsChecked == true;
            }
            pixel.Tolerance = uint.Parse(Tolerance.Text, CultureInfo.InvariantCulture);
            if (pixel.Coordinates == PixelCoordinates.ClientLogical) pixel.ReferenceDpi = uint.Parse(Dpi.Text, CultureInfo.InvariantCulture);
            result.Pixel = pixel;
        }
        else throw new ArgumentException("Choose a supported condition source.");
        WaitValidation.Validate(result);
        return result;
    }
    public void ApplyStyles(Style field, Style button)
    {
        foreach (var text in new[] { Executable, Class, Title, X, Y, Rgb, Tolerance, Dpi, Timeout, Stability, Poll }) text.Style = field;
        _testButton.Style = button;
        foreach (var pickerButton in _pickerButtons) pickerButton.Style = button;
        ApplyAdditionalSourceStyles(field, button);
    }
    public void Committed() { IsDirty = false; Feedback.Text = "Condition saved. Undo is available."; }
    public void CancelTest()
    {
        CancelPicker();
        CancelAdditionalSourceWork();
        if (_test is { } test)
        {
            _testGeneration++;
            if (!_disposed) Feedback.Text = "Condition test cancelled.";
            _ = test.CancelAsync().ContinueWith(task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    internal async Task TestAsync()
    {
        if (_disposed) return;
        CancelPicker();
        if (_test is not null) { CancelTest(); return; }
        var generation = ++_testGeneration;
        try
        {
            var condition = Read();
            using var cancel = new CancellationTokenSource(); _test = cancel;
            _testButton.Content = "Cancel condition test";
            Feedback.Text = "Testing this condition only…";
            var duration = TimeSpan.FromMicroseconds(Math.Min(condition.TimeoutUs, 5_000_000));
            var result = await _runner.RunAsync(condition, duration, cancel.Token, progress => DispatcherQueue.TryEnqueue(() =>
            { if (CurrentTest(cancel, generation)) Feedback.Text = $"{progress.Remaining.TotalSeconds:0.0}s remaining · {progress.Observation}"; }));
            if (CurrentTest(cancel, generation)) Feedback.Text = (result.Satisfied ? "Satisfied. " : "Not satisfied. ") + result.Detail;
        }
        catch (OperationCanceledException) { if (!_disposed && generation == _testGeneration) Feedback.Text = "Condition test cancelled."; }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        { if (!_disposed && generation == _testGeneration) Feedback.Text = error.Message; }
        finally { _test = null; _testButton.Content = "Test condition · up to 5 s"; }
    }
    private bool CurrentTest(CancellationTokenSource test, long generation) =>
        !_disposed && generation == _testGeneration && ReferenceEquals(_test, test) && !test.IsCancellationRequested;
    private static ulong Duration(string text, decimal multiplier)
    {
        var value = decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture) * multiplier;
        if (value < 0 || value > ulong.MaxValue || value != decimal.Truncate(value)) throw new ArgumentException("Enter a nonnegative time with whole microsecond precision.");
        return (ulong)value;
    }
    private void UpdateFields()
    {
        var pixel = Source.SelectedIndex == 1;
        var triggerCount = pixel ? 3 : 4;
        if (Trigger.Items.Count != triggerCount)
        {
            var selected = Trigger.SelectedIndex;
            Trigger.ItemsSource = pixel ? new[] { "Is true", "Becomes true", "Changes from starting value" }
                : new[] { "Is true", "Becomes true", "Changes from starting value", "New matching window" };
            Trigger.SelectedIndex = selected < triggerCount ? selected : _populating ? -1 : 0;
        }
        _pixel.Visibility = Coordinates.Visibility = pixel ? Visibility.Visible : Visibility.Collapsed;
        WindowRule.Visibility = Source.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        _target.Visibility = Source.SelectedIndex == 0 || pixel && Coordinates.SelectedIndex != 0 ? Visibility.Visible : Visibility.Collapsed;
        Any.Visibility = pixel ? Visibility.Collapsed : Visibility.Visible;
        if (pixel && !_populating) Any.IsChecked = false;
        Dpi.Visibility = pixel && Coordinates.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        Rgb.Visibility = NotEqual.Visibility = Trigger.SelectedIndex == (int)WaitTrigger.Changes ? Visibility.Collapsed : Visibility.Visible;
        _help.Text = pixel ? "Samples one visible screen pixel. Covered or minimized client targets are unavailable. Logical offsets scale with DPI, not window size."
            : "Specify at least one target field. Multiple matches need a narrower selector or Allow any matching window.";
        _help.Text += " Waiting does not focus a window or redirect subsequent input. Timeout or an unavailable target stops playback.";
        if (Trigger.SelectedIndex == (int)WaitTrigger.Changes)
            _help.Text += pixel ? " Changes compares with the first valid runtime pixel, using the channel tolerance."
                : " Changes requires Visible or Foreground and a single target; Exists and Absent are not supported for this trigger.";
        UpdateAdditionalSourceFields();
    }
    private void Change()
    {
        if (_populating) return;
        CancelTest(); IsDirty = true; Feedback.Text = "Unapplied condition changes"; UpdateSentence(); Changed?.Invoke();
    }
    private void UpdateSentence()
    {
        try { _sentence.Text = WaitConditionText.Describe(Read()); }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        { _sentence.Text = "Complete the condition fields below. " + error.Message; }
    }
    private TextBox Field(string name, string value)
    {
        var field = new TextBox { Header = name, Text = value, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(field, name); field.TextChanging += (_, _) => Change(); return field;
    }
    private ComboBox Choice(string name, string[] values, int selected)
    {
        var field = new ComboBox { Header = name, ItemsSource = values, SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(field, name); field.SelectionChanged += (_, _) => Change(); return field;
    }
    private CheckBox Check(string name, bool value)
    {
        var field = new CheckBox { Content = name, IsChecked = value };
        field.Checked += (_, _) => Change(); field.Unchecked += (_, _) => Change(); return field;
    }
    public void Dispose() { _disposed = true; CancelTest(); }

    partial void InitializeAdditionalSources(WaitCondition condition);
    partial void ReadAdditionalSource(WaitCondition result, ref bool handled);
    partial void UpdateAdditionalSourceFields();
    partial void ApplyAdditionalSourceStyles(Style field, Style button);
    partial void CancelAdditionalSourceWork();
}
