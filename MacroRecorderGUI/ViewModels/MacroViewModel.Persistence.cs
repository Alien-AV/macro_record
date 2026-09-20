using System.Collections.Specialized;
using System.ComponentModel;
using Google.Protobuf;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using ProtobufGenerated;

namespace MacroRecorderGUI.ViewModels;

public sealed partial class MacroViewModel
{
    private readonly HashSet<InputEvent> _observedEvents = [];
    private byte[]? _originalBytes;
    private ProtobufInputEventList? _wireTemplate;
    private long _savedVersion = -1;
    private bool _isDraft = true;
    public event EventHandler? Changed;
    public Guid RecordingId { get; private set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SavedAt { get; private set; }
    public long ChangeVersion { get; private set; }
    public bool IsDirty => ChangeVersion != _savedVersion;
    public RecordingSaveState SaveState { get; private set; } = RecordingSaveState.Unsaved;
    public string? SaveError { get; private set; }
    public bool IsDraft
    {
        get => _isDraft;
        set { if (_isDraft == value) return; _isDraft = value; OnPropertyChanged(); MarkChanged(); }
    }

    private void DetachPersistenceTracking()
    {
        Events.CollectionChanged -= EventsChanged;
        foreach (var input in _observedEvents) input.PropertyChanged -= InputChanged;
        _observedEvents.Clear();
    }

    private void EventsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var input in _observedEvents) input.PropertyChanged -= InputChanged;
            _observedEvents.Clear();
            foreach (var input in Events)
                if (_observedEvents.Add(input)) input.PropertyChanged += InputChanged;
        }
        if (args.OldItems is not null)
            foreach (InputEvent input in args.OldItems)
                if (!Events.Contains(input) && _observedEvents.Remove(input)) input.PropertyChanged -= InputChanged;
        if (args.NewItems is not null)
            foreach (InputEvent input in args.NewItems)
                if (_observedEvents.Add(input)) input.PropertyChanged += InputChanged;
        _originalBytes = null;
        MarkChanged();
    }

    private void InputChanged(object? sender, PropertyChangedEventArgs args)
    {
        _originalBytes = null;
        MarkChanged();
    }

    private void MarkChanged()
    {
        ChangeVersion++;
        SaveState = SavedAt is null ? RecordingSaveState.Unsaved : RecordingSaveState.Dirty;
        SaveError = null;
        NotifySaveState();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal byte[] SnapshotBytes()
    {
        if (_originalBytes is not null) return _originalBytes.ToArray();
        var wire = _wireTemplate?.Clone() ?? new ProtobufInputEventList();
        wire.InputEvents.Clear();
        wire.InputEvents.AddRange(Events.Select(input => input.OriginalProtobufInputEvent.Clone()));
        return wire.ToByteArray();
    }

    internal void Restore(StoredRecording recording)
    {
        RecordingId = recording.Metadata.Id;
        CreatedAt = recording.Metadata.CreatedAt;
        Name = recording.Metadata.Name;
        IsDraft = recording.Metadata.IsDraft;
        _wireTemplate = ProtobufInputEventList.Parser.ParseFrom(recording.MacroBytes);
        PopulateEventCollectionWithNewEvents(_wireTemplate.InputEvents.Select(input => InputEvent.CreateInputEvent(input.Clone())));
        _originalBytes = recording.MacroBytes.ToArray();
        SavedAt = recording.Metadata.UpdatedAt;
        _savedVersion = recording.Recovered ? -1 : ChangeVersion;
        SaveState = recording.Recovered ? RecordingSaveState.Dirty : RecordingSaveState.Saved;
        SaveError = recording.Recovered ? "Recovered the previous saved copy. Save to repair the current copy." : null;
        OnPropertyChanged(nameof(RecordingId));
        NotifySaveState();
    }

    internal void BeginSave() { SaveState = RecordingSaveState.Saving; SaveError = null; NotifySaveState(); }
    internal void Saved(long version, DateTimeOffset savedAt)
    {
        _savedVersion = version;
        SavedAt = savedAt;
        SaveState = IsDirty ? RecordingSaveState.Dirty : RecordingSaveState.Saved;
        SaveError = null;
        NotifySaveState();
    }
    internal void SaveFailed(Exception error) { SaveState = RecordingSaveState.Failed; SaveError = error.Message; NotifySaveState(); }
    private void NotifySaveState()
    {
        OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(SaveState));
        OnPropertyChanged(nameof(SaveError)); OnPropertyChanged(nameof(SavedAt));
    }
}
