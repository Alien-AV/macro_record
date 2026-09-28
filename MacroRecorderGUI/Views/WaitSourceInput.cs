using System.Globalization;

namespace MacroRecorderGUI.Views;

/// <summary>Parses advanced authoring fields without inspecting a target process.</summary>
internal static class WaitSourceInput
{
    public static ulong Address(string text, string field)
    {
        var value = text.Trim();
        var hexadecimal = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (hexadecimal) value = value[2..];
        if (value.Length == 0 || !ulong.TryParse(value,
            hexadecimal ? NumberStyles.AllowHexSpecifier : NumberStyles.None,
            CultureInfo.InvariantCulture, out var parsed))
            throw new ArgumentException($"{field}: enter an unsigned decimal number or hexadecimal number beginning with 0x.");
        return parsed;
    }

    public static long Offset(string text, string field)
    {
        var value = text.Trim();
        var negative = value.StartsWith('-');
        if (negative || value.StartsWith('+')) value = value[1..];
        ulong magnitude;
        try { magnitude = Address(value, field); }
        catch (ArgumentException) { throw InvalidOffset(field); }
        if (negative && magnitude == (ulong)long.MaxValue + 1) return long.MinValue;
        if (magnitude > long.MaxValue) throw InvalidOffset(field);
        return negative ? -(long)magnitude : (long)magnitude;
    }

    public static long[] Offsets(string text, int maximum)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        // A delimiter always denotes another hop; an unfinished entry is a draft error.
        var entries = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split([',', ';', '\n']);
        if (entries.Length > maximum)
            throw new ArgumentException($"Pointer offsets: enter at most {maximum} offsets in dereference order.");
        return entries.Select((value, index) => Offset(value, $"Pointer offset {index + 1}")).ToArray();
    }

    public static string FormatAddress(ulong address) => $"0x{address:X}";
    public static string FormatOffset(long offset) => offset < 0
        ? "-0x" + (offset == long.MinValue ? 1UL << 63 : (ulong)-offset).ToString("X", CultureInfo.InvariantCulture)
        : $"0x{offset:X}";

    private static ArgumentException InvalidOffset(string field) => new(
        $"{field}: enter a signed 64-bit decimal or hexadecimal offset, for example 32, 0x20, or -0x10.");
}
