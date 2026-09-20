using System.Runtime.InteropServices;
using MacroRecorderGUI.Utils;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace MacroRecorderGUI.Views;

/// <summary>Only constructed for an explicitly requested run. Never activates the recording target.</summary>
public sealed partial class RunController : Window
{
    private DesktopThemeMonitor? _themeMonitor;
    private bool _closed;
    private bool _allowClose;
    private bool _recording;
    public event EventHandler? StopRequested;

    public RunController(ElementTheme theme, string name, string shortcut)
    {
        InitializeComponent();
        WindowIcon.Apply(AppWindow);
        StopButton.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, args) =>
        {
            // Minimize controller input in capture. Raw input can arrive before this UI event.
            if (args.GetCurrentPoint(StopButton).Properties.IsLeftButtonPressed)
            { StopRequested?.Invoke(this, EventArgs.Empty); args.Handled = true; }
        }), true);
        ControllerRoot.RequestedTheme = theme;
        RecordingName.Text = name;
        ShortcutLabel.Text = shortcut;
        _themeMonitor = new DesktopThemeMonitor(AppWindow.Id, UpdateTitleBar);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
        AppWindow.Resize(new SizeInt32((int)(480 * dpi), (int)(380 * dpi)));
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(workArea.X + workArea.Width - AppWindow.Size.Width - (int)(24 * dpi), workArea.Y + (int)(24 * dpi)));
        AppWindow.Closing += Window_Closing;
        Closed += (_, _) => { _closed = true; _themeMonitor?.Dispose(); _themeMonitor = null; };
        UpdateTitleBar();
    }

    public void ShowWithoutActivation() => AppWindow.Show(false);
    public void SetTheme(ElementTheme theme) { ControllerRoot.RequestedTheme = theme; UpdateTitleBar(); }
    internal void SetState(RunControllerPresentation state, string shortcut)
    {
        if (_closed) return;
        StateLabel.Text = state.State; ElapsedLabel.Text = state.Clock; DetailLabel.Text = state.Detail; RunNote.Text = state.Note;
        ClockCaption.Text = state.Countdown ? "Starts in" : "Elapsed";
        DetailLabel.Visibility = string.IsNullOrEmpty(state.Detail) ? Visibility.Collapsed : Visibility.Visible;
        StopLabel.Text = state.Countdown ? "Cancel" : state.Recording ? "Stop recording" : "Stop playback";
        StopButton.IsEnabled = state.CanStop;
        ShortcutLabel.Text = shortcut;
        _recording = state.Recording;
        UpdateStateBrushes();
    }
    private void UpdateStateBrushes()
    {
        var color = DesignResources.Brush(ControllerRoot, _recording ? "MacroRedBrush" : "MacroAmberBrush");
        StateLabel.Foreground = color; StateDot.Fill = color;
    }
    public void Finish()
    {
        if (_closed) return;
        _allowClose = true;
        Close();
    }
    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);
    private void Window_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        StopRequested?.Invoke(this, EventArgs.Empty);
    }
    private void Root_Loaded(object sender, RoutedEventArgs e) => UpdateTitleBar();
    private void Root_ThemeChanged(FrameworkElement sender, object args) => UpdateTitleBar();
    private void UpdateTitleBar()
    {
        if (_closed || _themeMonitor is null || ControllerRoot is null) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || _themeMonitor is null) return;
            UpdateStateBrushes();
            if (!AppWindowTitleBar.IsCustomizationSupported()) return;
            var colors = TitleBarPalette.ForTheme(ControllerRoot.ActualTheme == ElementTheme.Dark,
                _themeMonitor.HighContrast, OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000));
            var bar = AppWindow.TitleBar;
            bar.BackgroundColor = bar.InactiveBackgroundColor = bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = colors.Background;
            bar.ForegroundColor = bar.ButtonForegroundColor = colors.Foreground;
            bar.InactiveForegroundColor = bar.ButtonInactiveForegroundColor = colors.InactiveForeground;
            bar.ButtonHoverBackgroundColor = colors.HoverBackground; bar.ButtonHoverForegroundColor = colors.Foreground;
            bar.ButtonPressedBackgroundColor = colors.PressedBackground; bar.ButtonPressedForegroundColor = colors.Foreground;
        });
    }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
}
