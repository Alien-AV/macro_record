using System.Text.Json;
using MacroRecorderGUI.Utils;

namespace MacroRecorderGUI.Models;

internal sealed record RunPreferenceData(RecordingOptions Recording, IReadOnlyDictionary<Guid, PlaybackOptions> Playback,
    string? Warning = null);

internal interface IRunPreferenceStore
{
    Task<RunPreferenceData> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(RunPreferenceData preferences, CancellationToken cancellationToken);
}

/// <summary>Settings are independent of recording documents and their save revisions.</summary>
internal sealed class RunPreferences(IRunPreferenceStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RunPreferenceData? _data;
    public bool IsLoaded => _data is not null;
    public string? Warning => _data?.Warning;
    public RecordingOptions Recording => _data?.Recording ?? new();
    public PlaybackOptions PlaybackFor(Guid id) => _data?.Playback.GetValueOrDefault(id) ?? new();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_data is not null) return;
            var data = await store.LoadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _data = data;
        }
        finally { _gate.Release(); }
    }

    public Task SaveRecordingAsync(RecordingOptions options, CancellationToken cancellationToken)
    {
        options.Validate();
        return SaveAsync(data => data with { Recording = options }, cancellationToken);
    }

    public Task SavePlaybackAsync(Guid id, PlaybackOptions options, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("A recording ID is required.", nameof(id));
        options.Validate();
        if (options.RepeatCount is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(options));
        return SaveAsync(data => data with { Playback = new Dictionary<Guid, PlaybackOptions>(data.Playback) { [id] = options } }, cancellationToken);
    }

    private async Task SaveAsync(Func<RunPreferenceData, RunPreferenceData> change, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var updated = change(_data!) with { Warning = null };
            await store.SaveAsync(updated, cancellationToken);
            // A committed write is authoritative even if cancellation arrived after the atomic replace.
            _data = updated;
        }
        finally { _gate.Release(); }
    }
}

/// <summary>Version 1: shared recording options and playback options keyed by stable library ID.</summary>
internal sealed class RunPreferenceStore(string? path = null) : IRunPreferenceStore
{
    private readonly string _path = Path.GetFullPath(path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacroRecorder", "run-preferences.json"));

    public async Task<RunPreferenceData> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(_path, cancellationToken));
            var root = json.RootElement;
            ValidateObject(root);
            if (root.GetProperty("Schema").GetInt32() != 1)
                throw new InvalidDataException("This run preferences version is not supported.");
            var warnings = new List<string>();
            var recording = new RecordingOptions();
            if (root.TryGetProperty("Recording", out var record))
            {
                try
                {
                    ValidateObject(record);
                    recording = record.Deserialize<RecordingOptions>() ?? throw new JsonException();
                    recording.Validate();
                }
                catch (Exception error) when (InvalidPreference(error))
                { recording = new(); warnings.Add("Recording options were invalid; using a 3s countdown and captured timing."); }
            }
            var playback = new Dictionary<Guid, PlaybackOptions>();
            if (root.TryGetProperty("Playback", out var entries))
            {
                foreach (var entry in entries.EnumerateObject())
                {
                    try
                    {
                        if (!Guid.TryParse(entry.Name, out var id) || id == Guid.Empty || playback.ContainsKey(id))
                            throw new JsonException();
                        // Infinite playback requires a complete, explicitly saved entry.
                        ValidateObject(entry.Value);
                        var options = entry.Value.Deserialize<PlaybackPreference>() ?? throw new JsonException();
                        if (options.RepeatUntilStopped && (options.Speed is null || options.RepeatCount is null || options.CountdownSeconds is null))
                            throw new JsonException();
                        var value = new PlaybackOptions { Speed = options.Speed ?? 1, RepeatCount = options.RepeatCount ?? 1,
                            Countdown = TimeSpan.FromSeconds(options.CountdownSeconds ?? 3), RepeatUntilStopped = options.RepeatUntilStopped };
                        value.Validate();
                        if (value.RepeatCount is < 1 or > 1000) throw new JsonException();
                        playback.Add(id, value);
                    }
                    catch (Exception error) when (InvalidPreference(error))
                    {
                        if (Guid.TryParse(entry.Name, out var id) && id != Guid.Empty) playback[id] = new();
                        warnings.Add("Playback options were invalid; using 1× · Once · 3s delay.");
                    }
                }
            }
            return new(recording, playback, warnings.Count == 0 ? null : string.Join(" ", warnings.Distinct()));
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(new(), new Dictionary<Guid, PlaybackOptions>());
        }
        catch (Exception error) when (InvalidPreference(error) || error is IOException or UnauthorizedAccessException)
        {
            return new(new(), new Dictionary<Guid, PlaybackOptions>(), "Run preferences could not be read; using safe defaults (1× · Once · 3s delay).");
        }
    }

    public async Task SaveAsync(RunPreferenceData preferences, CancellationToken cancellationToken)
    {
        preferences.Recording.Validate();
        foreach (var (id, options) in preferences.Playback)
        {
            if (id == Guid.Empty || options.RepeatCount is < 1 or > 1000) throw new InvalidDataException("Invalid playback preference.");
            options.Validate();
        }
        var document = new { Schema = 1, preferences.Recording, Playback = preferences.Playback.ToDictionary(pair => pair.Key,
            pair => new PlaybackPreference(pair.Value.Speed, pair.Value.RepeatCount, pair.Value.Countdown.TotalSeconds, pair.Value.RepeatUntilStopped)) };
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await AtomicFile.WriteAsync(_path, JsonSerializer.SerializeToUtf8Bytes(document), cancellationToken: cancellationToken);
    }

    private static bool InvalidPreference(Exception error) => error is JsonException or ArgumentException or InvalidOperationException
        or KeyNotFoundException or OverflowException or FormatException;
    private static void ValidateObject(JsonElement value)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name)) throw new JsonException("Duplicate preference field.");
    }
    private sealed record PlaybackPreference(double? Speed, int? RepeatCount, double? CountdownSeconds, bool RepeatUntilStopped);
}
