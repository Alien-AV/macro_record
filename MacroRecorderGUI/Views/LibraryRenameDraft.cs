using MacroRecorderGUI.Models;

namespace MacroRecorderGUI.Views;

/// <summary>Uncommitted name text belongs to the rename UI, never to the recording.</summary>
public sealed class LibraryRenameDraft(string name)
{
    public string Name { get; set; } = name;
    public bool IsSaving { get; private set; }
    public bool IsClosed { get; private set; }
    public string? Error { get; private set; }

    public bool Cancel()
    {
        if (IsSaving) return false;
        IsClosed = true;
        return true;
    }

    public async Task<bool> SaveAsync(Func<string, Task> persist)
    {
        if (IsClosed || IsSaving) return false;
        Error = null;
        try
        {
            var nameToSave = RecordingNames.Validate(Name);
            IsSaving = true;
            await persist(nameToSave);
            IsClosed = true;
            return true;
        }
        catch (Exception error) { Error = error.Message; return false; }
        finally { IsSaving = false; }
    }
}
