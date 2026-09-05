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
        macro.ConvertMouseEventsToAbsolutePositioning();

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

        viewModel.SynchronizeTabOrder([thirdMacro, firstMacro, secondMacro]);
        viewModel.SelectedTabIndex = 1;
        viewModel.CloseTab(thirdMacro);

        CollectionAssert.AreEqual(
            new[] { firstMacro, secondMacro },
            viewModel.MacroTabs.ToArray());
        Assert.AreSame(firstMacro, viewModel.ActiveMacro);
        Assert.AreEqual(0, viewModel.SelectedTabIndex);
    }
}
