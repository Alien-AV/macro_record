using System.Globalization;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;

namespace MacroRecorderGUI.Editor;

public enum ActionField { Wait, Duration, DestinationX, DestinationY, FixedDelay }

/// <summary>Auto-commit drafts belong to an input identity, not the list's mutable selection.</summary>
public sealed class ActionFieldEdits(MacroViewModel macro)
{
    private readonly Dictionary<ActionField, string> _text = [];
    private readonly Dictionary<ActionField, string> _errors = [];
    private InputEvent? _anchor;
    public string? Error => _errors.Values.FirstOrDefault();
    public bool HasDrafts => _text.Count != 0;

    public void Select(RecordedAction? action, bool reset = false)
    {
        if (!reset && ReferenceEquals(_anchor, action?.First)) return;
        var index = _anchor is null ? -1 : macro.Events.IndexOf(_anchor);
        // Editing a wait can merge adjacent movements. Surviving input still owns its drafts.
        if (!reset && action is not null && index >= action.Start && index < action.End) return;
        _text.Clear(); _errors.Clear(); _anchor = action?.First;
    }

    public void Change(ActionField field, string text) => _text[field] = text;
    public void SelectDelay(InputEvent input)
    {
        if (ReferenceEquals(_anchor, input)) return;
        _text.Clear(); _errors.Clear(); _anchor = input;
    }
    public string Text(ActionField field, string modelText) => _text.GetValueOrDefault(field, modelText);
    public void CancelDestination()
    {
        _text.Remove(ActionField.DestinationX); _text.Remove(ActionField.DestinationY);
        _errors.Remove(ActionField.DestinationX);
    }

    public bool TryCommitAll()
    {
        foreach (var field in new[] { ActionField.Wait, ActionField.FixedDelay, ActionField.Duration, ActionField.DestinationX })
            if (!TryCommit(field)) return false;
        return true;
    }

    public bool TryCommit(ActionField field)
    {
        if (field == ActionField.DestinationY) field = ActionField.DestinationX;
        if (!_text.ContainsKey(field) && (field != ActionField.DestinationX || !_text.ContainsKey(ActionField.DestinationY))) return true;
        try
        {
            var editor = macro.Editor;
            editor.Refresh();
            var index = _anchor is null ? -1 : macro.Events.IndexOf(_anchor);
            if (index < 0 || editor.Projection.ActionAt(index) is not { } action)
                throw new ArgumentException("The edited action is no longer available. Undo or select an action before editing.");
            switch (field)
            {
                case ActionField.FixedDelay:
                    if (_anchor is not DelayEvent delay) throw new ArgumentException("Select a Delay step.");
                    editor.SetFixedDelay(delay, checked((ulong)ParseTime(_text[field])));
                    break;
                case ActionField.Wait:
                    editor.SetLeadingDelay(_anchor!, checked((ulong)ParseTime(_text[field])));
                    break;
                case ActionField.Duration:
                    editor.SetDuration(action, ParseTime(_text[field]));
                    break;
                case ActionField.DestinationX:
                    var end = editor.Projection.Samples[action.End - 1].Position;
                    var x = int.Parse(Text(ActionField.DestinationX, end?.X.ToString("0", CultureInfo.InvariantCulture) ?? ""), CultureInfo.InvariantCulture);
                    var y = int.Parse(Text(ActionField.DestinationY, end?.Y.ToString("0", CultureInfo.InvariantCulture) ?? ""), CultureInfo.InvariantCulture);
                    editor.SetDestination(action, x, y);
                    _text.Remove(ActionField.DestinationY);
                    break;
            }
            _text.Remove(field); _errors.Remove(field);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or OverflowException or FormatException)
        {
            _errors[field] = error.Message;
            return false;
        }
    }

    internal static System.Numerics.BigInteger ParseTime(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : TimeText.ParseSeconds(text);
}
