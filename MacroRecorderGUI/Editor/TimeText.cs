using System.Globalization;
using System.Numerics;

namespace MacroRecorderGUI.Editor;

public static class TimeText
{
    public static string Human(BigInteger microseconds)
    {
        if (microseconds == 0) return "0ms";
        if (microseconds < 1_000) return $"{microseconds.ToString(CultureInfo.InvariantCulture)}µs";
        var milliseconds = (microseconds + 500) / 1_000;
        return milliseconds < 1_000 ? $"{milliseconds.ToString(CultureInfo.InvariantCulture)}ms"
            : $"{DecimalUnits((microseconds + 5_000) / 10_000, 2)}s";
    }
    public static string Seconds(BigInteger microseconds) => DecimalUnits(microseconds, 6);
    private static string DecimalUnits(BigInteger value, int places)
    {
        var divisor = BigInteger.Pow(10, places);
        var whole = BigInteger.DivRem(value, divisor, out var remainder);
        return remainder == 0 ? whole.ToString(CultureInfo.InvariantCulture)
            : whole.ToString(CultureInfo.InvariantCulture) + "." + remainder.ToString("D" + places, CultureInfo.InvariantCulture).TrimEnd('0');
    }
    public static BigInteger ParseSeconds(string text)
    {
        var pieces = text.Trim().Split('.');
        if (pieces.Length > 2 || pieces[0].Length == 0 || pieces.Any(p => p.Any(c => c is < '0' or > '9'))
            || pieces.Length == 2 && pieces[1].Length > 6)
            throw new ArgumentException("Enter non-negative seconds with at most six decimal places (use a decimal point).");
        return BigInteger.Parse(pieces[0], CultureInfo.InvariantCulture) * 1_000_000
            + (pieces.Length == 2 ? BigInteger.Parse(pieces[1].PadRight(6, '0'), CultureInfo.InvariantCulture) : 0);
    }
}
