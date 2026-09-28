using System.Text;
using ProtobufGenerated;

namespace MacroRecorder.Waiting;

internal static class TextPredicates
{
    public const int MaximumCharacters = 32768;

    public static string Normalize(string text, TextWhitespace whitespace)
    {
        if (text.Length > MaximumCharacters) throw new ArgumentException("Text exceeds the 64 KiB read limit.");
        if (whitespace == TextWhitespace.PreserveWhitespace) return text;
        var builder = new StringBuilder(text.Length);
        var gap = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character)) { gap = builder.Length != 0; continue; }
            if (gap) builder.Append(' ');
            gap = false; builder.Append(character);
        }
        return builder.ToString();
    }

    public static bool Equal(string a, string b, TextPredicate predicate) => string.Equals(
        Normalize(a, predicate.Whitespace), Normalize(b, predicate.Whitespace),
        predicate.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static WaitObservation Observe(TextReadResult result, TextPredicate predicate)
    {
        if (result.Status != ReadStatus.Success) return new(result.Status == ReadStatus.Error ? ObservationState.Error : ObservationState.Unavailable,
            result.Detail.Length > 0 ? result.Detail : "Text is unavailable.");
        if (result.Text.Length > MaximumCharacters || result.Identity.Length == 0)
            return new(ObservationState.Unavailable, "Text read exceeded its bound or has no stable identity.");
        var actual = Normalize(result.Text, predicate.Whitespace);
        var expected = Normalize(predicate.Expected, predicate.Whitespace);
        var comparison = predicate.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var match = predicate.Comparison switch
        {
            TextComparison.TextEquals => string.Equals(actual, expected, comparison),
            TextComparison.TextNotEquals => !string.Equals(actual, expected, comparison),
            TextComparison.TextContains => actual.Contains(expected, comparison),
            TextComparison.TextNotContains => !actual.Contains(expected, comparison),
            _ => throw new ArgumentException("Unsupported text comparison.")
        };
        return new(match ? ObservationState.Match : ObservationState.NoMatch, match ? "Text condition matched." : "Text condition did not match.",
            result.Identity, Text: actual);
    }
}
