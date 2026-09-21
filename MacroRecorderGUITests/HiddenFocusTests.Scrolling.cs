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
            var workspace = Field<ScrollViewer>(editor, "WorkspaceScroller");

            foreach (var (width, height) in new[] { (1200d, 800d), (1200d, 260d), (600d, 800d), (420d, 300d), (900d, 500d), (1200d, 800d) })
            {
                LayoutControl(editor, width, height);
                Call(editor, "ResizeWorkspace");
                editor.UpdateLayout();
                var rawScroller = Descendants(raw).OfType<ScrollViewer>().First();
                Assert.IsTrue(rawScroller.ScrollableHeight > 0, "The sample list must actually overflow.");
                Assert.IsTrue(inspector.ScrollableHeight > 0, "Expanded exact input must overflow the bounded inspector.");

                var rawBar = VerticalBar(rawScroller);
                var inspectorBar = VerticalBar(inspector);
                var workspaceBar = VerticalBar(workspace);
                var rawBounds = Bounds(rawBar, editor);
                var inspectorBounds = Bounds(inspectorBar, editor);
                var workspaceBounds = Bounds(workspaceBar, editor);
                Assert.IsTrue(rawBar.ActualWidth > 0 && rawBar.ActualHeight > 0);
                Assert.IsTrue(inspectorBar.ActualWidth > 0 && inspectorBar.ActualHeight > 0);
                Assert.AreEqual(rawScroller.ScrollableHeight, rawBar.Maximum, 0.01);
                Assert.AreEqual(inspector.ScrollableHeight, inspectorBar.Maximum, 0.01);
                Assert.IsTrue(rawBar.IsEnabled && inspectorBar.IsEnabled);
                Assert.IsTrue(rawBar.IsHitTestVisible && inspectorBar.IsHitTestVisible);
                Assert.IsFalse(ClickAwayFocus.IsBackgroundPress(editor, rawBar, true, false));
                Assert.IsFalse(ClickAwayFocus.IsBackgroundPress(editor, inspectorBar, true, false),
                    "Dragging the inspector scrollbar must not commit or clear an editor draft.");
                Assert.IsTrue(rawBounds.Right <= inspectorBounds.Left,
                    $"Raw and inspector scrollbar hit areas overlap at {width}x{height}: {rawBounds}; {inspectorBounds}");
                if (workspace.ScrollableHeight > 0)
                    Assert.IsTrue(inspectorBounds.Right <= workspaceBounds.Left,
                        $"Inspector and page scrollbar hit areas overlap at {width}x{height}.");

                // Hidden HWNDs do not advance compositor scrolling. Check the actual
                // template ranges and disjoint hit columns; physical dragging is a smoke test.
                Assert.IsTrue(rawScroller.ViewportHeight <= inspector.ViewportHeight);
                Assert.IsTrue(rawScroller.ViewportHeight <= workspace.ViewportHeight);
                Assert.AreSame(selection, raw.SelectedItem);
                Assert.AreEqual("unfinished raw draft", delay.Text);
                Assert.AreEqual("unfinished action draft", wait.Text);
                Assert.AreEqual(2, delay.SelectionStart); Assert.AreEqual(5, delay.SelectionLength);
                CollectionAssert.AreEqual(before, macro.SnapshotBytes());
                Assert.IsFalse(IsWindowVisible(hwnd));
            }
        }
        finally { window.Close(); }
    }
}
