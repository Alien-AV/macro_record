namespace MacroRecorderGUI.Views;

internal sealed class ShellFeedback
{
    private string? _lastEditorStatus;
    private string? _message;
    private string? _editorStatus;
    public string? Text => _editorStatus ?? _message;
    public void Report(string message)
    {
        _message = message;
        _editorStatus = null;
        _lastEditorStatus = null;
    }
    public void ReportEditor(string? status)
    {
        if (status == _lastEditorStatus) return;
        _lastEditorStatus = status;
        _editorStatus = string.IsNullOrWhiteSpace(status) ? null : status;
    }
    public void Clear() { _message = _editorStatus = _lastEditorStatus = null; }
}
