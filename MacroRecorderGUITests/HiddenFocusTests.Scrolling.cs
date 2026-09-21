using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace MacroRecorderGUITests;

public sealed partial class HiddenFocusTests
{
    private static void LayoutControl(FrameworkElement control, double width, double height)
    {
        control.Width = width; control.Height = height;
        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));
        control.UpdateLayout();
    }

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static ScrollBar VerticalBar(ScrollViewer scroller) => Descendants(scroller).OfType<ScrollBar>()
        .Single(bar => bar.Orientation == Orientation.Vertical && ScrollOwner(bar) == scroller);

    private static ScrollViewer? ScrollOwner(DependencyObject child)
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer scroller) return scroller;
        return null;
    }

    private static Rect Bounds(FrameworkElement element, UIElement root) =>
        element.TransformToVisual(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static void CheckEditorScrollbars()
    {
        using var macro = new MacroViewModel("Scroll regression", new FakePlaybackEngine());
        for (var i = 0; i < 80; i++)
            macro.AddEvent(new MouseEvent(i, i, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 100 });
        using var editor = new MacroTabContent { DataContext = macro };
        var window = new Window { Content = editor };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        try
        {
            Assert.IsFalse(IsWindowVisible(hwnd));
            Call(editor, "Attach");
            var actions = Field<ListView>(editor, "ActionsList");
            actions.SelectedIndex = 0;
            Call(editor, "SetRawOpen", true);
            Field<CheckBox>(editor, "ShowSamples").IsChecked = true;
            var raw = Field<ListView>(editor, "RawList");
            var delay = Field<TextBox>(editor, "RawDelay");
            var wait = Field<TextBox>(editor, "WaitInput");
            delay.Text = "unfinished raw draft";
            wait.Text = "unfinished action draft";
            delay.Select(2, 5);
            var selection = raw.SelectedItem;
            var before = macro.SnapshotBytes();
            var inspector = Field<ScrollViewer>(editor, "InspectorScroller");
            var fields = Field<ScrollViewer>(editor, "RawFieldsScroller");
            var workspace = Field<Grid>(editor, "Workspace");
            Assert.IsNull(ScrollOwner(workspace), "The editor must not have a whole-page scroller.");
            Assert.IsNull(ScrollOwner(raw), "The exact-input table must not be nested in an inspector scroller.");

            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark, ElementTheme.Default })
            foreach (var (width, height) in new[] { (1200d, 800d), (1200d, 260d), (600d, 800d), (420d, 300d), (900d, 500d), (1200d, 800d) })
            {
                editor.RequestedTheme = theme;
                LayoutControl(editor, width, height);
                Call(editor, "ResizeWorkspace");
                editor.UpdateLayout();
                var rawScroller = Descendants(raw).OfType<ScrollViewer>().First();
                Assert.IsTrue(rawScroller.ScrollableHeight > 0, "The sample list must actually overflow.");
                Assert.AreEqual(Visibility.Collapsed, inspector.Visibility, "Exact input replaces the action details viewport.");
                Assert.IsTrue(fields.ScrollableHeight > 0, "The raw fields have their own bounded viewport beside the table, never around it.");

                var rawBar = VerticalBar(rawScroller);
                var inspectorBar = VerticalBar(fields);
                var rawBounds = Bounds(rawBar, editor);
                var inspectorBounds = Bounds(inspectorBar, editor);
                Assert.IsTrue(rawBar.ActualWidth > 0 && rawBar.ActualHeight > 0);
                Assert.IsTrue(inspectorBar.ActualWidth > 0 && inspectorBar.ActualHeight > 0);
                Assert.AreEqual(rawScroller.ScrollableHeight, rawBar.Maximum, 0.01);
                Assert.AreEqual(fields.ScrollableHeight, inspectorBar.Maximum, 0.01);
                Assert.IsTrue(rawBar.IsEnabled && inspectorBar.IsEnabled);
                Assert.IsTrue(rawBar.IsHitTestVisible && inspectorBar.IsHitTestVisible);
                Assert.IsFalse(ClickAwayFocus.IsBackgroundPress(editor, rawBar, true, false));
                Assert.IsFalse(ClickAwayFocus.IsBackgroundPress(editor, inspectorBar, true, false),
                    "Dragging the inspector scrollbar must not commit or clear an editor draft.");
                Assert.IsTrue(rawBounds.Bottom <= inspectorBounds.Top,
                    $"Table and field scrollbar hit areas overlap at {width}x{height}: {rawBounds}; {inspectorBounds}");
                Assert.IsTrue(inspectorBounds.Bottom <= height + 1);
                Assert.IsTrue(workspace.ActualHeight <= height);
                Assert.AreEqual(Visibility.Visible, Field<Border>(editor, "InspectorPanel").Visibility);
                Assert.AreEqual(width < 900 ? Visibility.Collapsed : Visibility.Visible, Field<Border>(editor, "ListPanel").Visibility);

                // Hidden HWNDs do not advance compositor scrolling. Check the actual
                // template ranges and disjoint hit columns; physical dragging is a smoke test.
                Assert.IsTrue(rawScroller.ViewportHeight <= workspace.ActualHeight);
                Assert.IsTrue(fields.ViewportHeight <= workspace.ActualHeight);
                Assert.AreSame(selection, raw.SelectedItem);
                Assert.AreEqual("unfinished raw draft", delay.Text);
                Assert.AreEqual("unfinished action draft", wait.Text);
                Assert.AreEqual(2, delay.SelectionStart); Assert.AreEqual(5, delay.SelectionLength);
                CollectionAssert.AreEqual(before, macro.SnapshotBytes());
                Assert.IsFalse(IsWindowVisible(hwnd));
            }

            // Exercise the actual compiled high-contrast resources without changing OS preferences.
            var resources = editor.Resources.MergedDictionaries[0];
            var light = resources.ThemeDictionaries["Light"];
            var contrast = (ResourceDictionary)resources.ThemeDictionaries["HighContrast"];
            var contrastCopy = new ResourceDictionary();
            foreach (var resource in contrast) contrastCopy[resource.Key] = new SolidColorBrush(((SolidColorBrush)resource.Value).Color);
            resources.ThemeDictionaries.Remove("Light"); resources.ThemeDictionaries["Light"] = contrastCopy;
            editor.RequestedTheme = ElementTheme.Dark; editor.RequestedTheme = ElementTheme.Light;
            Call(editor, "ShowPane", true);
            Call(editor, "SetRawOpen", false);
            LayoutControl(editor, 420, 420);
            var ink = (SolidColorBrush)contrast["DesignedEditorInk"];
            Assert.AreEqual(ink.Color, ((SolidColorBrush)Call(editor, "BrushResource", "Ink")!).Color);
            var textControls = Descendants(editor).ToArray();
            foreach (var block in textControls.OfType<TextBlock>()) block.FontSize *= 2;
            foreach (var control in textControls.OfType<Control>().Where(control => control is TextBox or Button or CheckBox)) control.FontSize *= 2;
            LayoutControl(editor, 420, 420);
            Assert.IsTrue(inspector.ScrollableHeight > 0, "Enlarged text stays reachable in the bounded details pane.");
            Assert.IsTrue(Bounds(VerticalBar(inspector), editor).Bottom <= 421);
            Call(editor, "SetRawOpen", true); LayoutControl(editor, 420, 420);
            Assert.IsTrue(fields.ScrollableHeight > 0);
            Assert.IsTrue(Bounds(VerticalBar(fields), editor).Bottom <= 421);
            Call(editor, "ShowPane", false); LayoutControl(editor, 420, 420);
            Assert.AreEqual(Visibility.Collapsed, Field<Border>(editor, "InspectorPanel").Visibility);
            Assert.IsTrue(Bounds(Field<Button>(editor, "AddAction"), editor).Bottom <= 421, "Add stays anchored when text is enlarged.");
            resources.ThemeDictionaries.Remove("Light"); resources.ThemeDictionaries["Light"] = light;
            CollectionAssert.AreEqual(before, macro.SnapshotBytes());
            Assert.AreEqual("unfinished raw draft", delay.Text);
            Assert.AreEqual("unfinished action draft", wait.Text);
            Assert.IsFalse(IsWindowVisible(hwnd));
        }
        finally { window.Close(); }
    }
}
