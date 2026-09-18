using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;
using MouseInputEvent = MacroRecorderGUI.Event.MouseEvent;

namespace MacroRecorderGUITests;

[TestClass]
public class MacroViewModelTests
{
    [TestMethod]
    public void RelativeMousePositionsAreConvertedToAbsoluteCoordinates()
    {
        var macro = new MacroViewModel("test", new FakePlaybackEngine());
        var firstEvent = new MouseInputEvent(100, 200, MouseActionTypeFlags.Move);
        var secondEvent = new MouseInputEvent(5, -7, MouseActionTypeFlags.Move)
        {
            RelativePosition = true
        };

        macro.AddEvent(firstEvent);
        macro.AddEvent(secondEvent);
        macro.Editor.ConvertAnchoredEstimate();

        Assert.IsFalse(secondEvent.RelativePosition);
        Assert.AreEqual(105, secondEvent.X);
        Assert.AreEqual(193, secondEvent.Y);
    }

    [TestMethod]
    public void ManuallyCreatedEventIsInsertedAfterTheLastSelectedEvent()
    {
        var macro = new MacroViewModel("test", new FakePlaybackEngine());
        var firstEvent = new MouseInputEvent(1, 2, MouseActionTypeFlags.Move);
        var secondEvent = new MouseInputEvent(3, 4, MouseActionTypeFlags.Move);
        macro.AddEvent(firstEvent);
        macro.AddEvent(secondEvent);
        macro.ReplaceSelection([firstEvent]);

        macro.CreateKeyboardEventManually();

        Assert.HasCount(3, macro.Events);
        Assert.IsInstanceOfType<KeyboardEvent>(macro.Events[1]);
        Assert.AreSame(secondEvent, macro.Events[2]);
    }

    [TestMethod]
    public void ReorderingAndClosingTabsKeepsTheSelectedMacroActive()
    {
        var viewModel = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine());
        var firstMacro = viewModel.ActiveMacro!;
        var secondMacro = viewModel.AddNewTab();
        var thirdMacro = viewModel.AddNewTab();

        viewModel.SynchronizeTabs([thirdMacro, firstMacro, secondMacro], firstMacro);
        viewModel.CloseTab(thirdMacro);

        CollectionAssert.AreEqual(
            new[] { firstMacro, secondMacro },
            viewModel.MacroTabs.ToArray());
        Assert.AreSame(firstMacro, viewModel.ActiveMacro);
        Assert.AreEqual(0, viewModel.SelectedTabIndex);
    }

    [TestMethod]
    public void CommandsAndRecordingFollowTheSelectedTabAfterReorder()
    {
        var recordEngine = new FakeRecordEngine();
        var playbackEngine = new FakePlaybackEngine();
        var viewModel = new FakeMainWindowViewModel(recordEngine, playbackEngine);
        var firstMacro = viewModel.ActiveMacro!;
        var secondMacro = viewModel.AddNewTab();
        var thirdMacro = viewModel.AddNewTab();
        var firstEvent = new MouseInputEvent(1, 2, MouseActionTypeFlags.Move);
        firstMacro.AddEvent(firstEvent);

        viewModel.SynchronizeTabs([thirdMacro, firstMacro, secondMacro], firstMacro);
        viewModel.ActiveMacro!.PlayMacro();

        CollectionAssert.AreEqual(new[] { thirdMacro, firstMacro, secondMacro }, viewModel.MacroTabs.ToArray());
        Assert.AreSame(firstMacro, viewModel.ActiveMacro);
        Assert.IsTrue(playbackEngine.PlayedEvents.Contains(firstEvent));

        var recordedEvent = FakeRecordEngine.MakeKeyboardEvent(65, false, 123);
        viewModel.StartRecording();
        recordEngine.PushEvent(recordedEvent);
        Assert.AreSame(recordedEvent, firstMacro.Events[1].OriginalProtobufInputEvent);
        Assert.HasCount(0, secondMacro.Events);
        Assert.HasCount(0, thirdMacro.Events);

        viewModel.ActiveMacro.Clear();
        Assert.HasCount(0, firstMacro.Events);
        viewModel.CloseTab(thirdMacro);
        Assert.AreSame(firstMacro, viewModel.ActiveMacro);
        Assert.AreEqual(0, viewModel.SelectedTabIndex);
    }

    [TestMethod]
    public void ReorderIgnoresTransientRemovalAndPreservesSelectionUntilTheViewSelectsATab()
    {
        var viewModel = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine());
        var firstMacro = viewModel.ActiveMacro!;
        var secondMacro = viewModel.AddNewTab();
        var thirdMacro = viewModel.AddNewTab();
        viewModel.SelectedTabIndex = 0;

        viewModel.SynchronizeTabs([secondMacro, thirdMacro], secondMacro);
        Assert.AreSame(firstMacro, viewModel.ActiveMacro);
        CollectionAssert.AreEqual(new[] { firstMacro, secondMacro, thirdMacro }, viewModel.MacroTabs.ToArray());

        viewModel.SynchronizeTabs([secondMacro, thirdMacro, firstMacro], null);
        Assert.AreSame(firstMacro, viewModel.ActiveMacro);
        Assert.AreEqual(2, viewModel.SelectedTabIndex);

        viewModel.SynchronizeTabs([secondMacro, thirdMacro, firstMacro], thirdMacro);
        Assert.AreSame(thirdMacro, viewModel.ActiveMacro);
        Assert.AreEqual(1, viewModel.SelectedTabIndex);
    }
}
