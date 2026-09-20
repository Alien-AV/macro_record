using System.Collections.Specialized;
using System.ComponentModel;
using Google.Protobuf;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using ProtobufGenerated;

namespace MacroRecorderGUI.ViewModels;

public sealed partial class MacroViewModel
{
    private readonly HashSet<InputEvent> _observedEvents = [];
    private byte[]? _originalBytes;
    private ProtobufInputEventList? _wireTemplate;
    private RecordingDocument _document = new() { Events = [] };
    public IReadOnlyList<PointerOriginBoundary> PointerOrigins => Array.AsReadOnly(_document.Origins);
    public bool HasOriginMetadata => _document.IsExtended;
    public bool CanRecoverBeforeOriginAdoption => _document.BeforeOriginAdoption is not null;
    public event EventHandler? OriginsChanged;
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
        ReindexOrigins(args);
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
        return (_document with { Events = wire.ToByteArray() }).Write();
    }

    internal void Restore(StoredRecording recording)
    {
        RecordingId = recording.Metadata.Id;
        CreatedAt = recording.Metadata.CreatedAt;
        Name = recording.Metadata.Name;
        IsDraft = recording.Metadata.IsDraft;
        var document = RecordingDocument.Read(recording.MacroBytes);
        _wireTemplate = document.ParseEvents();
        PopulateEventCollectionWithNewEvents(_wireTemplate.InputEvents.Select(input => InputEvent.CreateInputEvent(input.Clone())));
        RestoreOriginState(document);
        _originalBytes = recording.MacroBytes.ToArray();
        SavedAt = recording.Metadata.UpdatedAt;
        _savedVersion = recording.Recovered ? -1 : ChangeVersion;
        SaveState = recording.Recovered ? RecordingSaveState.Dirty : RecordingSaveState.Saved;
        SaveError = recording.Recovered ? "Recovered the previous saved copy. Save to repair the current copy." : null;
        OnPropertyChanged(nameof(RecordingId));
        NotifySaveState();
    }

    internal RecordingDocument OriginState => _document;

    internal void RestoreOriginState(RecordingDocument state)
    {
        _document = state;
        _originalBytes = null;
        MarkChanged();
        OriginsChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(PointerOrigins));
        OnPropertyChanged(nameof(HasOriginMetadata));
        OnPropertyChanged(nameof(CanRecoverBeforeOriginAdoption));
    }

    internal void AddCaptureOrigin(PointerPosition? position)
    {
        RestoreOriginState(_document with { IsExtended = true,
            Origins = [.. _document.Origins, new PointerOriginBoundary(Events.Count, position)] });
    }

    private void ReindexOrigins(NotifyCollectionChangedEventArgs change)
    {
        if (_document.Origins.Length == 0) return;
        var origins = _document.Origins;
        if (change.Action == NotifyCollectionChangedAction.Reset) origins = [];
        else
        {
            if (change.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Move)
                origins = origins.Select(origin => origin with { EventIndex = origin.EventIndex
                    - Math.Clamp(origin.EventIndex - change.OldStartingIndex, 0, change.OldItems!.Count) }).ToArray();
            if (change.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Move)
                origins = origins.Select(origin => origin.EventIndex > change.NewStartingIndex
                    ? origin with { EventIndex = checked(origin.EventIndex + change.NewItems!.Count) } : origin).ToArray();
        }
        _document = _document with { Origins = origins };
    }

    public string? OriginAdoptionDescription()
    {
        if (_document.Origins.Any(origin => origin.EventIndex == 0) || Events.FirstOrDefault() is not MouseEvent
            { RelativePosition: false, ActionType: Common.MouseActionTypeFlags.Move, MouseData: 0 } first) return null;
        return $"Use raw event 1: absolute Move to ({first.X}, {first.Y}) physical pixels on the {(first.MappedToVirtualDesktop ? "virtual desktop" : "primary screen")} as the starting point. "
            + $"Remove this one Move from the action list and preserve its {first.TimeSinceLastEvent} µs delay in setup metadata. All later input and delays stay unchanged. "
            + "This might be a real recorded movement; the file has no synthetic marker. Recorded starting point reproduces it as setup; Current pointer relocates it. "
            + "Undo restores the change. A complete pre-adoption copy is also retained in the saved recording for recovery.";
    }

    public void AdoptFirstPositionAsOrigin()
    {
        if (OriginAdoptionDescription() is null) throw new InvalidOperationException("The first raw event must be an ordinary absolute Move with no button or wheel data, and no existing initial origin.");
        var before = SnapshotBytes();
        var first = (MouseEvent)Events[0];
        var origin = new PointerOriginBoundary(0, new(first.X, first.Y), first.TimeSinceLastEvent,
            Convert.ToBase64String(first.OriginalProtobufInputEvent.ToByteArray()));
        Editor.Execute("Use first position as origin", () =>
        {
            Events.RemoveAt(0);
            RestoreOriginState(_document with { IsExtended = true, Origins = [origin, .. _document.Origins], BeforeOriginAdoption = before });
        });
    }

    public void RecoverBeforeOriginAdoption()
    {
        var bytes = _document.BeforeOriginAdoption ?? throw new InvalidOperationException("No pre-adoption copy is available.");
        var legacy = RecordingDocument.Read(bytes);
        Editor.Execute("Recover before origin adoption", () =>
        {
            Events.Clear();
            foreach (var input in legacy.ParseEvents().InputEvents) Events.Add(InputEvent.CreateInputEvent(input.Clone()));
            RestoreOriginState(legacy);
        });
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
