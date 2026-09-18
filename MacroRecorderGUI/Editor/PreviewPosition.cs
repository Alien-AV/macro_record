using System.Numerics;

namespace MacroRecorderGUI.Editor;

/// <summary>Keeps the requested slider position separate from integer-microsecond quantization.</summary>
public sealed class PreviewPosition
{
    public const int Maximum = 1000;
    public BigInteger Time { get; private set; }
    public BigInteger Duration { get; private set; }
    public double Value { get; private set; }

    public void Scrub(double value, BigInteger duration)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        ValidateDuration(duration);
        Duration = duration;
        Value = Math.Clamp(value, 0, Maximum);
        // Interpret the slider's binary fraction exactly, without narrowing a potentially huge total.
        var bits = BitConverter.DoubleToInt64Bits(Value);
        var significand = new BigInteger(bits & ((1L << 52) - 1));
        var exponent = (int)((bits >> 52) & 0x7ff);
        if (exponent == 0) exponent = -1074;
        else { significand += 1L << 52; exponent -= 1075; }
        var numerator = duration * significand;
        var denominator = new BigInteger(Maximum);
        if (exponent >= 0) numerator <<= exponent;
        else denominator <<= -exponent;
        Time = numerator / denominator;
    }

    public void SeekTime(BigInteger time, BigInteger duration)
    {
        ValidateDuration(duration);
        Duration = duration;
        Time = BigInteger.Clamp(time, BigInteger.Zero, duration);
        // Only the bounded quotient reaches floating point. Nanosteps are ample for a visual slider.
        const long precision = 1_000_000_000;
        Value = duration == 0 ? 0 : (double)(Time * Maximum * precision / duration) / precision;
    }

    public void RefreshDuration(BigInteger duration)
    {
        // Redraws/capture refreshes with an unchanged total must retain the user's requested thumb position,
        // even when several slider steps map to the same microsecond (including a zero-duration stream).
        if (duration != Duration) SeekTime(Time, duration);
    }

    private static void ValidateDuration(BigInteger duration)
    {
        if (duration < 0) throw new ArgumentOutOfRangeException(nameof(duration));
    }
}
