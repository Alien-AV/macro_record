namespace MacroRecorderGUI.Editor;

/// <summary>Tracks edits from synchronous control change events; model population never creates a draft.</summary>
public sealed class InspectorDrafts<T> where T : notnull
{
    private readonly HashSet<T> _edited = [];
    private int _populationDepth;
    public void Changing(T field) { if (_populationDepth == 0) _edited.Add(field); }
    public bool Populate(T field, Action assign)
    {
        if (_edited.Contains(field)) return false;
        _populationDepth++;
        try { assign(); }
        finally { _populationDepth--; }
        return true;
    }
    public void Clear() => _edited.Clear();
    public void Remove(T field) => _edited.Remove(field);
}
