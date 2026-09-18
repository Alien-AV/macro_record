using System.Numerics;
using MacroRecorderGUI.Editor;

namespace MacroRecorderGUITests;

[TestClass]
public class PreviewPositionTests
{
    [TestMethod]
    [DataRow(0L)]
    [DataRow(1L)]
    [DataRow(7L)]
    [DataRow(999L)]
    [DataRow(1001L)]
    [DataRow(1500L)]
    [DataRow(1000001L)]
    [DataRow(1234567L)]
    public void ArrowStepsAdvanceInBothDirectionsWithoutQuantizationFeedback(long duration)
        => VerifyArrowSteps(duration);

    [TestMethod]
    public void ArrowStepsWorkForTotalsBeyondFloatingPointRange()
        => VerifyArrowSteps(BigInteger.Pow(10, 400) + 567);

    private static void VerifyArrowSteps(BigInteger duration)
    {
        var position = new PreviewPosition();
        position.RefreshDuration(duration);
        for (var step = 1; step <= PreviewPosition.Maximum; step++)
        {
            position.Scrub(position.Value + 1, duration);
            position.RefreshDuration(duration); // Same refresh used by the view before redrawing.
            Assert.AreEqual((double)step, position.Value);
            Assert.AreEqual(duration * step / PreviewPosition.Maximum, position.Time);
        }
        for (var step = PreviewPosition.Maximum - 1; step >= 0; step--)
        {
            position.Scrub(position.Value - 1, duration);
            position.RefreshDuration(duration);
            Assert.AreEqual((double)step, position.Value);
            Assert.AreEqual(duration * step / PreviewPosition.Maximum, position.Time);
        }
    }

    [TestMethod]
    public void MouseFractionSurvivesRedrawAndConvertsExactlyForHugeTotals()
    {
        var duration = BigInteger.Pow(10, 400) + 567;
        var position = new PreviewPosition();
        position.Scrub(123.5, duration);
        position.RefreshDuration(duration);
        Assert.AreEqual(123.5, position.Value);
        Assert.AreEqual(duration * 247 / 2000, position.Time);
        position.Scrub(999.5, 1);
        Assert.AreEqual(BigInteger.Zero, position.Time);
        Assert.AreEqual(999.5, position.Value);
        position.Scrub(1000, 1);
        Assert.AreEqual(BigInteger.One, position.Time);
    }

    [TestMethod]
    public void PlaybackAndActionSeeksTakeOverFromTheScrubbedPosition()
    {
        var position = new PreviewPosition();
        position.Scrub(1, 1234567);
        Assert.AreEqual(new BigInteger(1234), position.Time);
        Assert.AreEqual(1d, position.Value);
        position.SeekTime(0, 1234567);
        Assert.AreEqual(0d, position.Value);
        var huge = BigInteger.Pow(10, 400);
        position.SeekTime(huge / 2, huge);
        Assert.AreEqual(500d, position.Value);
        position.SeekTime(huge + 1, huge);
        Assert.AreEqual(1000d, position.Value);
        Assert.AreEqual(huge, position.Time);
    }

    [TestMethod]
    public void DurationChangesPreserveTimeAndClampAtTheNewEnd()
    {
        var position = new PreviewPosition();
        position.Scrub(500, 2000);
        position.RefreshDuration(4000);
        Assert.AreEqual(new BigInteger(1000), position.Time);
        Assert.AreEqual(250d, position.Value);
        position.RefreshDuration(100);
        Assert.AreEqual(new BigInteger(100), position.Time);
        Assert.AreEqual(1000d, position.Value);
        position.RefreshDuration(0);
        Assert.AreEqual(BigInteger.Zero, position.Time);
        Assert.AreEqual(0d, position.Value);
    }

    [TestMethod]
    public void ScrubClampsEndpointsAndHandlesSubnormalMouseValues()
    {
        var position = new PreviewPosition();
        position.Scrub(-1, 17);
        Assert.AreEqual(0d, position.Value); Assert.AreEqual(BigInteger.Zero, position.Time);
        position.Scrub(1001, 17);
        Assert.AreEqual(1000d, position.Value); Assert.AreEqual(new BigInteger(17), position.Time);
        position.Scrub(double.Epsilon, BigInteger.One << 1074);
        Assert.AreEqual(double.Epsilon, position.Value); Assert.AreEqual(BigInteger.Zero, position.Time);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => position.Scrub(double.NaN, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => position.Scrub(double.PositiveInfinity, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => position.Scrub(0, -1));
    }
}
