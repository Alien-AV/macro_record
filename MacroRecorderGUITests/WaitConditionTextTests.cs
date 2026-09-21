using Google.Protobuf;
using MacroRecorderGUI.Editor;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WaitConditionTextTests
{
    [TestMethod]
    [DataRow(WindowTest.Exists, "existence")]
    [DataRow(WindowTest.Absent, "absence")]
    [DataRow(WindowTest.Visible, "visibility")]
    [DataRow(WindowTest.Foreground, "foreground")]
    public void ChangedWindowDescriptionsNameTheActualPredicate(WindowTest test, string predicate)
    {
        var condition = ConditionalWaitTests.Condition();
        condition.Window.Test = test; condition.Trigger = WaitTrigger.Changes;
        var before = condition.ToByteArray();
        StringAssert.Contains(WaitConditionText.Describe(condition), $"initial {predicate} state");
        CollectionAssert.AreEqual(before, condition.ToByteArray(), "Describing an imported condition must not repair or rewrite it.");
    }
}
