using System.Globalization;

namespace MacroRecorderGUI.Models;

public static class RecordingNames
{
    public static string NewDefault(DateTimeOffset localTime, IEnumerable<string> existingNames)
    {
        var stem = "Recording " + localTime.ToString("yyyy-MM-dd HH.mm", CultureInfo.InvariantCulture);
        var names = existingNames.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var name = stem;
        for (var suffix = 2; names.Contains(name); suffix++) name = $"{stem} ({suffix})";
        return name;
    }

    public static string Validate(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 200) throw new ArgumentException("Enter a recording name of 1–200 characters.");
        return trimmed;
    }
}
