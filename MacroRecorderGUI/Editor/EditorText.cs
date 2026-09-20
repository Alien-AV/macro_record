namespace MacroRecorderGUI.Editor;

public static class EditorText
{
    public static string Count(int count, string noun) => $"{count:N0} {noun}{(count == 1 ? "" : "s")}";
}
